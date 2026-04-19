using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using Naninovel;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UsoNatsuBetterController
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "cmarr.usonatsu.better-controller";
        public const string PluginName = "UsoNatsu Better Controller";
        public const string PluginVersion = "1.0.0";

        private const string DefaultLanguageButtonNumbers = "9";
        private const string DefaultSelectButtonNumbers = "6";
        private const string DefaultCancelButtonNumbers = "1";
        private const string DefaultLeftTriggerButtonNumbers = "";
        private const string DefaultRightTriggerButtonNumbers = "";
        private const string DefaultLeftShoulderScrollButtonNumbers = "4";
        private const string DefaultRightShoulderScrollButtonNumbers = "";
        private const string EnglishBindingName = "AutoPlay";
        private const string BacklogBindingName = "ShowBacklog";
        private const string ToggleSkipBindingName = "ToggleSkip";
        private const float FocusSeedInterval = 0.2f;
        private const float ScrollDeadzone = 0.2f;
        private const string DefaultLeftTriggerAxisCandidates = "LT,LeftTrigger,Left Trigger,9th axis,10th axis,11th axis,12th axis,13th axis";
        private const string DefaultRightTriggerAxisCandidates = "RT,RightTrigger,Right Trigger,9th axis,10th axis,11th axis,12th axis,13th axis";

        private static readonly string[] FocusUiHints = { "Pause", "Settings", "SaveLoadMenu", "BacklogPanel", "CustomTitleMenu", "SelectLanguagePanel" };
        private static readonly string[] BacklogUiHints = { "BacklogPanel" };
        private static readonly string[] SaveUiHints = { "SaveLoadMenu" };
        private static readonly string[] PauseUiHints = { "Pause" };

        private ConfigEntry<string> englishLocaleConfig;
        private ConfigEntry<string> japaneseLocaleConfig;
        private ConfigEntry<string> languageToggleButtonNumbersConfig;
        private ConfigEntry<string> selectButtonNumbersConfig;
        private ConfigEntry<string> cancelButtonNumbersConfig;
        private ConfigEntry<string> leftTriggerButtonNumbersConfig;
        private ConfigEntry<string> rightTriggerButtonNumbersConfig;
        private ConfigEntry<string> leftShoulderScrollButtonNumbersConfig;
        private ConfigEntry<string> rightShoulderScrollButtonNumbersConfig;
        private ConfigEntry<float> backlogScrollSpeedConfig;
        private ConfigEntry<string> leftTriggerAxisCandidatesConfig;
        private ConfigEntry<string> rightTriggerAxisCandidatesConfig;
        private HashSet<int> languageToggleButtons;
        private HashSet<int> selectButtons;
        private HashSet<int> cancelButtons;
        private HashSet<int> leftTriggerButtons;
        private HashSet<int> rightTriggerButtons;
        private HashSet<int> leftShoulderScrollButtons;
        private HashSet<int> rightShoulderScrollButtons;
        private bool initializationHooked;
        private bool initialized;
        private bool switchingLocale;
        private float nextFocusSeedTime;
        private new ManualLogSource Logger => base.Logger;

        private void Awake()
        {
            englishLocaleConfig = Config.Bind("Locales", "EnglishLocale", "en", "Preferred English locale code.");
            japaneseLocaleConfig = Config.Bind("Locales", "JapaneseLocale", "ja", "Preferred Japanese locale code.");
            languageToggleButtonNumbersConfig = Config.Bind("Input", "LanguageToggleButtonNumbers", DefaultLanguageButtonNumbers, "Comma-separated Unity legacy joystick button numbers that toggle language.");
            selectButtonNumbersConfig = Config.Bind("Input", "SelectButtonNumbers", DefaultSelectButtonNumbers, "Comma-separated Unity legacy joystick button numbers that open the save UI.");
            cancelButtonNumbersConfig = Config.Bind("Input", "CancelButtonNumbers", DefaultCancelButtonNumbers, "Comma-separated Unity legacy joystick button numbers that should close the pause UI.");
            leftTriggerButtonNumbersConfig = Config.Bind("Input", "LeftTriggerButtonNumbers", DefaultLeftTriggerButtonNumbers, "Comma-separated Unity legacy joystick button numbers that should scroll UI upward.");
            rightTriggerButtonNumbersConfig = Config.Bind("Input", "RightTriggerButtonNumbers", DefaultRightTriggerButtonNumbers, "Comma-separated Unity legacy joystick button numbers that should scroll UI downward.");
            leftShoulderScrollButtonNumbersConfig = Config.Bind("Input", "LeftShoulderScrollButtonNumbers", DefaultLeftShoulderScrollButtonNumbers, "Comma-separated Unity legacy joystick button numbers that should scroll UI upward while backlog/save UI is open.");
            rightShoulderScrollButtonNumbersConfig = Config.Bind("Input", "RightShoulderScrollButtonNumbers", DefaultRightShoulderScrollButtonNumbers, "Comma-separated Unity legacy joystick button numbers that should scroll UI downward while backlog/save UI is open.");
            backlogScrollSpeedConfig = Config.Bind("Input", "ScrollSpeed", 1.75f, "Scroll speed multiplier for trigger-based UI scrolling.");
            leftTriggerAxisCandidatesConfig = Config.Bind("Input", "LeftTriggerAxisCandidates", DefaultLeftTriggerAxisCandidates, "Comma-separated legacy Unity axis names to probe for left-trigger UI scrolling.");
            rightTriggerAxisCandidatesConfig = Config.Bind("Input", "RightTriggerAxisCandidates", DefaultRightTriggerAxisCandidates, "Comma-separated legacy Unity axis names to probe for right-trigger UI scrolling.");

            languageToggleButtons = ParseButtonNumbers(languageToggleButtonNumbersConfig.Value);
            selectButtons = ParseButtonNumbers(selectButtonNumbersConfig.Value);
            cancelButtons = ParseButtonNumbers(cancelButtonNumbersConfig.Value);
            leftTriggerButtons = ParseButtonNumbers(leftTriggerButtonNumbersConfig.Value);
            rightTriggerButtons = ParseButtonNumbers(rightTriggerButtonNumbersConfig.Value);
            leftShoulderScrollButtons = ParseButtonNumbers(leftShoulderScrollButtonNumbersConfig.Value);
            rightShoulderScrollButtons = ParseButtonNumbers(rightShoulderScrollButtonNumbersConfig.Value);

            TryInitialize();
            if (!Engine.Initialized)
            {
                Engine.OnInitializationFinished += HandleEngineInitialized;
                initializationHooked = true;
            }
        }

        private void OnDestroy()
        {
            if (initializationHooked)
            {
                Engine.OnInitializationFinished -= HandleEngineInitialized;
            }
        }

        private void Update()
        {
            if (!initialized || !Engine.Initialized)
            {
                return;
            }

            HandleLanguageToggleInput();
            HandleSaveUiInput();
            HandlePauseCancelInput();
            HandleSkipToggleInput();
            HandleUiScroll();
            SeedFocusForVisibleMenus();
        }

        private void HandleEngineInitialized()
        {
            Engine.OnInitializationFinished -= HandleEngineInitialized;
            initializationHooked = false;
            TryInitialize();
        }

        private void TryInitialize()
        {
            if (!Engine.Initialized || initialized)
            {
                return;
            }

            var inputManager = Engine.GetService<IInputManager>();
            if (inputManager == null)
            {
                Logger.LogWarning("Naninovel IInputManager was not available.");
                return;
            }

            RemapControllerBindings(inputManager);
            initialized = true;
        }

        private void HandleLanguageToggleInput()
        {
            if (switchingLocale || !WasAnyButtonPressed(languageToggleButtons))
            {
                return;
            }

            ToggleLocaleAsync().Forget();
        }

        private void HandleSaveUiInput()
        {
            if (!WasAnyButtonPressed(selectButtons))
            {
                return;
            }

            if (TryShowManagedUi(SaveUiHints))
            {
                nextFocusSeedTime = 0f;
            }
        }

        private void HandlePauseCancelInput()
        {
            if (!WasAnyButtonPressed(cancelButtons))
            {
                return;
            }

            var pauseUi = FindManagedUis(PauseUiHints, requireVisible: true, exactTypeMatch: false)
                .OrderByDescending(GetUiPriority)
                .FirstOrDefault();
            if (pauseUi == null)
            {
                return;
            }

            if (TryInvokeParameterless(pauseUi, "Hide"))
            {
                nextFocusSeedTime = 0f;
            }
        }

        private void HandleSkipToggleInput()
        {
            if (IsScrollableUiVisible() || !WasAnyButtonPressed(rightShoulderScrollButtons))
            {
                return;
            }

            var scriptPlayer = Engine.GetService<IScriptPlayer>();
            if (scriptPlayer == null)
            {
                return;
            }

            scriptPlayer.SetSkipEnabled(!scriptPlayer.SkipActive);
        }

        private void HandleUiScroll()
        {
            if (!IsScrollableUiVisible())
            {
                return;
            }

            var scrollValue = GetScrollInputValue();
            if (Mathf.Abs(scrollValue) < ScrollDeadzone)
            {
                return;
            }

            if (TryScrollVisibleUi(BacklogUiHints, scrollValue))
            {
                return;
            }

            TryScrollVisibleUi(SaveUiHints, scrollValue);
        }

        private void SeedFocusForVisibleMenus()
        {
            if (Time.unscaledTime < nextFocusSeedTime)
            {
                return;
            }

            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return;
            }

            if (eventSystem.currentSelectedGameObject != null && eventSystem.currentSelectedGameObject.activeInHierarchy)
            {
                return;
            }

            foreach (var hint in FocusUiHints)
            {
                var ui = FindVisibleManagedUi(hint);
                if (ui == null)
                {
                    continue;
                }

                var selectable = ui.GetComponentsInChildren<Selectable>(true)
                    .FirstOrDefault(candidate => candidate != null && candidate.IsActive() && candidate.interactable);
                if (selectable == null)
                {
                    continue;
                }

                eventSystem.SetSelectedGameObject(selectable.gameObject);
                nextFocusSeedTime = Time.unscaledTime + FocusSeedInterval;
                return;
            }
        }

        private async UniTaskVoid ToggleLocaleAsync()
        {
            switchingLocale = true;
            try
            {
                var localizationManager = Engine.GetService<ILocalizationManager>();
                if (localizationManager == null)
                {
                    Logger.LogWarning("ILocalizationManager was not available.");
                    return;
                }

                var availableLocales = localizationManager.GetAvailableLocales()?.ToArray() ?? Array.Empty<string>();
                if (availableLocales.Length == 0)
                {
                    Logger.LogWarning("No available locales were reported by the localization manager.");
                    return;
                }

                var englishLocale = ResolveLocale(availableLocales, englishLocaleConfig.Value);
                var japaneseLocale = ResolveLocale(availableLocales, japaneseLocaleConfig.Value);
                if (string.IsNullOrEmpty(englishLocale) || string.IsNullOrEmpty(japaneseLocale))
                {
                    Logger.LogWarning($"Unable to resolve English/Japanese locales from available locales: {string.Join(", ", availableLocales)}");
                    return;
                }

                var currentLocale = localizationManager.SelectedLocale;
                var nextLocale = string.Equals(currentLocale, englishLocale, StringComparison.OrdinalIgnoreCase)
                    ? japaneseLocale
                    : englishLocale;

                await localizationManager.SelectLocaleAsync(nextLocale);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to toggle locale: {ex}");
            }
            finally
            {
                switchingLocale = false;
            }
        }

        private void RemapControllerBindings(IInputManager inputManager)
        {
            RemapBindingButton(inputManager, EnglishBindingName, 4);
            RemapBindingButton(inputManager, BacklogBindingName, 2);
            RemoveJoystickButtons(inputManager, ToggleSkipBindingName);
        }

        private void RemapBindingButton(IInputManager inputManager, string bindingName, int targetButtonNumber)
        {
            var binding = GetBindings(inputManager).FirstOrDefault(candidate => string.Equals(candidate?.Name, bindingName, StringComparison.Ordinal));
            if (binding == null || binding.Keys == null)
            {
                return;
            }

            binding.Keys.RemoveAll(IsJoystickButtonKey);
            var targetKey = ParseJoystickButtonKey(targetButtonNumber);
            if (targetKey == null)
            {
                return;
            }

            if (!binding.Keys.Contains(targetKey.Value))
            {
                binding.Keys.Add(targetKey.Value);
            }
        }

        private void RemoveJoystickButtons(IInputManager inputManager, string bindingName)
        {
            var binding = GetBindings(inputManager).FirstOrDefault(candidate => string.Equals(candidate?.Name, bindingName, StringComparison.Ordinal));
            if (binding?.Keys == null)
            {
                return;
            }

            binding.Keys.RemoveAll(IsJoystickButtonKey);
        }

        private bool TryShowManagedUi(IEnumerable<string> exactTypeNames)
        {
            var candidates = FindManagedUis(exactTypeNames, requireVisible: false, exactTypeMatch: true)
                .OrderByDescending(GetUiPriority)
                .ToList();

            foreach (var candidate in candidates)
            {
                if (candidate == null)
                {
                    continue;
                }

                if (TryInvokeParameterless(candidate, "Show"))
                {
                    StartCoroutine(SeedFocusNextFrame());
                    return true;
                }
            }

            return false;
        }

        private bool TryScrollVisibleUi(string[] exactTypeNames, float scrollValue)
        {
            var ui = FindVisibleManagedUi(exactTypeNames);
            if (ui == null)
            {
                return false;
            }

            var scrollRect = ui.GetComponentInChildren<ScrollRect>(true);
            if (scrollRect == null || !scrollRect.IsActive())
            {
                return false;
            }

            var nextPosition = scrollRect.verticalNormalizedPosition + scrollValue * backlogScrollSpeedConfig.Value * Time.unscaledDeltaTime;
            scrollRect.verticalNormalizedPosition = Mathf.Clamp01(nextPosition);
            return true;
        }

        private IEnumerator SeedFocusNextFrame()
        {
            yield return null;
            nextFocusSeedTime = 0f;
            SeedFocusForVisibleMenus();
        }

        private MonoBehaviour FindVisibleManagedUi(params string[] nameHints)
        {
            return FindManagedUis(nameHints, requireVisible: true, exactTypeMatch: true)
                .OrderByDescending(GetUiPriority)
                .FirstOrDefault();
        }

        private IEnumerable<MonoBehaviour> FindManagedUis(IEnumerable<string> nameHints, bool requireVisible, bool exactTypeMatch = false)
        {
            var hints = nameHints?.Where(hint => !string.IsNullOrWhiteSpace(hint)).ToArray() ?? Array.Empty<string>();
            if (hints.Length == 0)
            {
                yield break;
            }

            foreach (var behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (behaviour == null)
                {
                    continue;
                }

                if (!LooksLikeManagedUi(behaviour))
                {
                    continue;
                }

                var typeName = behaviour.GetType().Name;
                if (exactTypeMatch)
                {
                    if (!hints.Any(hint => string.Equals(typeName, hint, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                }
                else if (!hints.Any(hint => ContainsIgnoreCase(typeName, hint)))
                {
                    continue;
                }

                if (requireVisible && !IsUiVisible(behaviour))
                {
                    continue;
                }

                yield return behaviour;
            }
        }

        private static int GetUiPriority(MonoBehaviour behaviour)
        {
            if (behaviour == null)
            {
                return 0;
            }

            var score = 0;
            if (IsUiVisible(behaviour))
            {
                score += 100;
            }

            if (behaviour.gameObject.activeInHierarchy)
            {
                score += 50;
            }

            if (ContainsIgnoreCase(behaviour.GetType().Name, "Save"))
            {
                score += 25;
            }

            if (ContainsIgnoreCase(behaviour.GetType().Name, "Pause"))
            {
                score += 25;
            }

            if (ContainsIgnoreCase(behaviour.GetType().Name, "SaveLoadMenu"))
            {
                score += 50;
            }

            if (ContainsIgnoreCase(behaviour.GetType().Name, "BacklogPanel"))
            {
                score += 50;
            }

            return score;
        }

        private static bool IsUiVisible(MonoBehaviour behaviour)
        {
            if (behaviour == null)
            {
                return false;
            }

            var visibleProperty = behaviour.GetType().GetProperty("Visible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (visibleProperty != null && visibleProperty.PropertyType == typeof(bool))
            {
                try
                {
                    return (bool)visibleProperty.GetValue(behaviour, null);
                }
                catch
                {
                    return behaviour.gameObject.activeInHierarchy;
                }
            }

            return behaviour.gameObject.activeInHierarchy;
        }

        private static bool TryInvokeParameterless(object target, string methodName)
        {
            if (target == null)
            {
                return false;
            }

            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (method == null)
            {
                return false;
            }

            try
            {
                method.Invoke(target, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IReadOnlyList<InputBinding> GetBindings(IInputManager inputManager)
        {
            if (inputManager is InputManager concreteManager && concreteManager.Configuration != null && concreteManager.Configuration.Bindings != null)
            {
                return concreteManager.Configuration.Bindings;
            }

            return Array.Empty<InputBinding>();
        }

        private float GetScrollInputValue()
        {
            var triggerValue = GetTriggerScrollValue();
            return Mathf.Abs(triggerValue) >= ScrollDeadzone ? triggerValue : 0f;
        }

        private float GetTriggerScrollValue()
        {
            var shoulderValue = GetShoulderScrollValue();
            if (Mathf.Abs(shoulderValue) >= ScrollDeadzone)
            {
                return shoulderValue;
            }

            var leftTrigger = GetTriggerValue(leftTriggerButtons, leftTriggerAxisCandidatesConfig.Value, invert: true);
            var rightTrigger = GetTriggerValue(rightTriggerButtons, rightTriggerAxisCandidatesConfig.Value, invert: false);
            return Mathf.Clamp(leftTrigger + rightTrigger, -1f, 1f);
        }

        private float GetShoulderScrollValue()
        {
            var leftShoulder = GetShoulderButtonValue(leftShoulderScrollButtons, invert: false);
            var rightShoulder = GetShoulderButtonValue(rightShoulderScrollButtons, invert: true);
            return Mathf.Clamp(leftShoulder + rightShoulder, -1f, 1f);
        }

        private float GetShoulderButtonValue(HashSet<int> buttonNumbers, bool invert)
        {
            if (!IsAnyButtonHeld(buttonNumbers))
            {
                return 0f;
            }

            return invert ? -1f : 1f;
        }

        private float GetTriggerValue(HashSet<int> buttonNumbers, string axisCandidates, bool invert)
        {
            if (IsAnyButtonHeld(buttonNumbers))
            {
                return invert ? -1f : 1f;
            }

            var triggerAxis = FindStrongestAxisValue(GetAxisCandidates(axisCandidates));
            if (string.IsNullOrEmpty(triggerAxis.axisName) || Mathf.Abs(triggerAxis.value) < ScrollDeadzone)
            {
                return 0f;
            }

            var normalizedValue = NormalizeTriggerValue(triggerAxis.value);
            if (normalizedValue < ScrollDeadzone)
            {
                return 0f;
            }

            var signedValue = invert ? -normalizedValue : normalizedValue;
            return signedValue;
        }

        private static float NormalizeTriggerValue(float value)
        {
            if (Mathf.Abs(value) >= 0.99f)
            {
                return 1f;
            }

            return Mathf.Clamp01(Mathf.Abs(value));
        }

        private static (string axisName, float value) FindStrongestAxisValue(IEnumerable<string> axisNames)
        {
            float strongestValue = 0f;
            string strongestAxis = null;

            foreach (var axisName in axisNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                float axisValue;
                if (!TryGetAxisRaw(axisName, out axisValue))
                {
                    continue;
                }

                if (Mathf.Abs(axisValue) > Mathf.Abs(strongestValue))
                {
                    strongestValue = axisValue;
                    strongestAxis = axisName;
                }
            }

            return (strongestAxis, strongestValue);
        }

        private bool IsScrollableUiVisible()
        {
            return FindVisibleManagedUi(BacklogUiHints) != null
                || FindVisibleManagedUi(SaveUiHints) != null;
        }

        private static bool LooksLikeManagedUi(MonoBehaviour behaviour)
        {
            if (behaviour == null)
            {
                return false;
            }

            if (HasVisibleProperty(behaviour))
            {
                return true;
            }

            var typeName = behaviour.GetType().Name;
            if (ContainsIgnoreCase(typeName, "Button") || ContainsIgnoreCase(typeName, "Toggle") || ContainsIgnoreCase(typeName, "Slider") || ContainsIgnoreCase(typeName, "Dropdown"))
            {
                return false;
            }

            return typeName.EndsWith("UI", StringComparison.Ordinal)
                || typeName.EndsWith("Panel", StringComparison.Ordinal)
                || typeName.EndsWith("Menu", StringComparison.Ordinal);
        }

        private static bool HasVisibleProperty(MonoBehaviour behaviour) => behaviour.GetType().GetProperty("Visible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;

        private static bool ContainsIgnoreCase(string source, string value)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
            {
                return false;
            }

            return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static HashSet<int> ParseButtonNumbers(string value)
        {
            var result = new HashSet<int>();
            foreach (var part in (value ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), out var buttonNumber) && buttonNumber >= 0)
                {
                    result.Add(buttonNumber);
                }
            }

            return result;
        }

        private static IEnumerable<string> GetAxisCandidates(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(candidate => candidate.Trim())
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate));
        }

        private static bool TryGetAxisRaw(string axisName, out float value)
        {
            try
            {
                value = Input.GetAxisRaw(axisName);
                return true;
            }
            catch
            {
                value = 0f;
                return false;
            }
        }

        private static bool WasAnyButtonPressed(HashSet<int> buttonNumbers)
        {
            if (buttonNumbers == null || buttonNumbers.Count == 0)
            {
                return false;
            }

            foreach (var buttonNumber in buttonNumbers)
            {
                foreach (var keyCode in EnumerateButtonKeyCodes(buttonNumber))
                {
                    if (Input.GetKeyDown(keyCode))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsAnyButtonHeld(HashSet<int> buttonNumbers)
        {
            if (buttonNumbers == null || buttonNumbers.Count == 0)
            {
                return false;
            }

            foreach (var buttonNumber in buttonNumbers)
            {
                foreach (var keyCode in EnumerateButtonKeyCodes(buttonNumber))
                {
                    if (Input.GetKey(keyCode))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<KeyCode> EnumerateButtonKeyCodes(int buttonNumber)
        {
            var generic = ParseKeyCode($"JoystickButton{buttonNumber}");
            if (generic != null)
            {
                yield return generic.Value;
            }

            for (var joystickIndex = 1; joystickIndex <= 8; joystickIndex++)
            {
                var specific = ParseKeyCode($"Joystick{joystickIndex}Button{buttonNumber}");
                if (specific != null)
                {
                    yield return specific.Value;
                }
            }
        }

        private static KeyCode? ParseJoystickButtonKey(int buttonNumber)
        {
            return ParseKeyCode($"JoystickButton{buttonNumber}");
        }

        private static KeyCode? ParseKeyCode(string value)
        {
            if (Enum.TryParse(value, true, out KeyCode keyCode))
            {
                return keyCode;
            }

            return null;
        }

        private static bool IsJoystickButtonKey(KeyCode keyCode)
        {
            var name = keyCode.ToString();
            return name.StartsWith("Joystick", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveLocale(IReadOnlyCollection<string> availableLocales, string preferredLocale)
        {
            if (availableLocales == null || availableLocales.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(preferredLocale))
            {
                var exactMatch = availableLocales.FirstOrDefault(locale => string.Equals(locale, preferredLocale, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(exactMatch))
                {
                    return exactMatch;
                }

                var prefixMatch = availableLocales.FirstOrDefault(locale => locale.StartsWith(preferredLocale, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(prefixMatch))
                {
                    return prefixMatch;
                }
            }

            return availableLocales.FirstOrDefault();
        }

    }
}
