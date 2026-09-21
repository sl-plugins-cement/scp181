using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Exiled.API.Features;

namespace Scp181.Visuals
{
    /// <summary>
    /// HintServiceMeow(HSM) 可选桥接器。通过反射调用 HSM 的 AddHint / RemoveHint，
    /// 未安装 HSM 时 IsAvailable=false，调用方回退到原生 ShowHint / Broadcast。
    /// </summary>
    public static class HsmHelper
    {
        private static Type _playerDisplayType;
        private static Type _hintType;
        private static Type _abstractHintType;
        private static MethodInfo _getDisplay;
        private static MethodInfo _addHint;
        private static MethodInfo _removeHint;
        private static PropertyInfo _idProperty;
        private static PropertyInfo _textProperty;
        private static PropertyInfo _xProperty;
        private static PropertyInfo _yProperty;
        private static PropertyInfo _fontSizeProperty;
        private static PropertyInfo _lineHeightProperty;
        private static PropertyInfo _alignmentProperty;
        private static PropertyInfo _verticalAlignmentProperty;
        private static PropertyInfo _syncSpeedProperty;
        private static object _centerAlignment;
        private static object _middleAlignment;
        private static object _fastSync;
        private static bool _available;
        private static bool _incompatible;
        private static bool _loggedUnavailable;
        /// <summary>已添加且仍需被引用删除的 hint 实例（key: playerId|group|id）。</summary>
        private static readonly Dictionary<string, object> _activeHints = new Dictionary<string, object>();

        public static bool IsAvailable => _available;

        public static bool TryInitialize()
        {
            if (_available) return true;
            if (_incompatible) return false;

            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => candidate.GetName().Name.StartsWith("HintServiceMeow", StringComparison.OrdinalIgnoreCase));
            if (assembly == null)
            {
                LogUnavailableOnce("HintServiceMeow is not loaded; falling back to native hints.");
                return false;
            }

            try
            {
                _playerDisplayType = assembly.GetType("HintServiceMeow.Core.Utilities.PlayerDisplay", true);
                _hintType = assembly.GetType("HintServiceMeow.Core.Models.Hints.Hint", true);
                _abstractHintType = assembly.GetType("HintServiceMeow.Core.Models.Hints.AbstractHint", true);
                Type alignmentType = assembly.GetType("HintServiceMeow.Core.Enum.HintAlignment", true);
                Type verticalType = assembly.GetType("HintServiceMeow.Core.Enum.HintVerticalAlign", true);
                Type syncType = assembly.GetType("HintServiceMeow.Core.Enum.HintSyncSpeed", true);

                _getDisplay = _playerDisplayType.GetMethod("Get", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ReferenceHub) }, null);
                _addHint = _playerDisplayType.GetMethod("AddHint", BindingFlags.Public | BindingFlags.Instance, null, new[] { _abstractHintType, typeof(string) }, null);
                _removeHint = _playerDisplayType.GetMethod("RemoveHint", BindingFlags.Public | BindingFlags.Instance, null, new[] { _abstractHintType, typeof(string) }, null);
                _idProperty = _abstractHintType.GetProperty("Id");
                _textProperty = _abstractHintType.GetProperty("Text");
                _fontSizeProperty = _abstractHintType.GetProperty("FontSize");
                _lineHeightProperty = _abstractHintType.GetProperty("LineHeight");
                _syncSpeedProperty = _abstractHintType.GetProperty("SyncSpeed");
                _xProperty = _hintType.GetProperty("XCoordinate");
                _yProperty = _hintType.GetProperty("YCoordinate");
                _alignmentProperty = _hintType.GetProperty("Alignment");
                _verticalAlignmentProperty = _hintType.GetProperty("YCoordinateAlign");
                _centerAlignment = Enum.Parse(alignmentType, "Center");
                _middleAlignment = Enum.Parse(verticalType, "Middle");
                _fastSync = Enum.Parse(syncType, "Fast");

                if (_getDisplay == null || _addHint == null || _removeHint == null || _idProperty == null ||
                    _textProperty == null || _fontSizeProperty == null || _lineHeightProperty == null ||
                    _syncSpeedProperty == null || _xProperty == null || _yProperty == null ||
                    _alignmentProperty == null || _verticalAlignmentProperty == null)
                    throw new MissingMemberException("Required HSM AddHint/RemoveHint surface not found.");

                _available = true;
                Log.Info("[Scp181:HSM] HintServiceMeow detected; hints will use AddHint.");
                return true;
            }
            catch (Exception ex)
            {
                _incompatible = true;
                LogUnavailableOnce($"HintServiceMeow is incompatible: {ex.GetBaseException().Message}");
                return false;
            }
        }

        /// <summary>通过 HSM 为单个玩家显示 hint（同 Player.Id + id 先移除再添加 = 更新）。成功返回 true。</summary>
        public static bool ShowHint(Player player, string content, float y, int fontSize, string id, string group)
        {
            if (player?.ReferenceHub == null || !TryInitialize())
                return false;
            try
            {
                object display = _getDisplay.Invoke(null, new object[] { player.ReferenceHub });
                string key = Key(player.Id, group, id);
                // 先删掉旧实例，避免同一 id 提示堆叠
                if (_activeHints.TryGetValue(key, out object old))
                    _removeHint.Invoke(display, new[] { old, group });

                object hint = Activator.CreateInstance(_hintType);
                _idProperty.SetValue(hint, id);
                _textProperty.SetValue(hint, content);
                _xProperty.SetValue(hint, 0f);
                _yProperty.SetValue(hint, y);
                _fontSizeProperty.SetValue(hint, fontSize);
                _lineHeightProperty.SetValue(hint, 8f);
                _alignmentProperty.SetValue(hint, _centerAlignment);
                _verticalAlignmentProperty.SetValue(hint, _middleAlignment);
                _syncSpeedProperty.SetValue(hint, _fastSync);

                _addHint.Invoke(display, new[] { hint, group });
                _activeHints[key] = hint;   // 记录实例，便于后续按引用删除
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[Scp181:HSM] ShowHint failed: {ex.GetBaseException().Message}");
                return false;
            }
        }

        public static void RemoveHint(Player player, string id, string group)
        {
            if (player?.ReferenceHub == null || !_available)
                return;
            try
            {
                string key = Key(player.Id, group, id);
                if (!_activeHints.TryGetValue(key, out object hint))
                    return;   // 无记录（未通过 HSM 添加过），无需删除

                object display = _getDisplay.Invoke(null, new object[] { player.ReferenceHub });
                _removeHint.Invoke(display, new[] { hint, group });
                _activeHints.Remove(key);
            }
            catch { }
        }

        private static string Key(int playerId, string group, string id)
            => playerId.ToString() + "|" + group + "|" + id;

        private static void LogUnavailableOnce(string reason)
        {
            if (_loggedUnavailable) return;
            _loggedUnavailable = true;
            Log.Warn($"[Scp181:HSM] {reason}");
        }
    }
}