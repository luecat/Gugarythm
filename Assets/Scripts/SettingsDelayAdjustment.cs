using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Gugarhythm
{
    public static class SettingsDelayAdjustment
    {
        public const double MinimumSeconds = -.3d;
        public const double MaximumSeconds = .3d;
        public const double StepSeconds = .001d;

        public static double Clamp(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0d;
            return Math.Clamp(value, MinimumSeconds, MaximumSeconds);
        }

        public static double Step(double value, double delta) => Clamp(Clamp(value) + delta);
    }

    public sealed class SettingsDelayHoldRepeater
    {
        public const double InitialDelaySeconds = .5d;
        public const double InitialRepeatIntervalSeconds = .125d;
        public const double FullSpeedHoldSeconds = 2.5d;
        public const double MinimumRepeatIntervalSeconds = 1d / 30d;

        double heldSeconds;
        double nextRepeatAtSeconds = InitialDelaySeconds;

        public static double RepeatIntervalSeconds(double heldDurationSeconds)
        {
            var progress = Math.Clamp(
                (heldDurationSeconds - InitialDelaySeconds) /
                (FullSpeedHoldSeconds - InitialDelaySeconds), 0d, 1d);
            return InitialRepeatIntervalSeconds +
                (MinimumRepeatIntervalSeconds - InitialRepeatIntervalSeconds) * progress;
        }

        public bool Advance(double deltaSeconds)
        {
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds <= 0d)
                return false;
            heldSeconds += deltaSeconds;
            if (heldSeconds + .0000001d < nextRepeatAtSeconds) return false;
            nextRepeatAtSeconds = heldSeconds + RepeatIntervalSeconds(heldSeconds);
            return true;
        }

        public void Reset()
        {
            heldSeconds = 0d;
            nextRepeatAtSeconds = InitialDelaySeconds;
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class SettingsDelayHoldButton : MonoBehaviour, IPointerDownHandler,
        IPointerUpHandler, IPointerExitHandler, ISubmitHandler
    {
        readonly SettingsDelayHoldRepeater repeater = new();
        Button button;
        Action stepAction;
        bool pointerHeld;

        public void Configure(Action action)
        {
            button = GetComponent<Button>();
            stepAction = action;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
            if (!CanInvoke()) return;
            pointerHeld = true;
            repeater.Reset();
            stepAction();
        }

        public void OnPointerUp(PointerEventData eventData) => StopHolding();

        public void OnPointerExit(PointerEventData eventData) => StopHolding();

        public void OnSubmit(BaseEventData eventData)
        {
            if (CanInvoke()) stepAction();
        }

        void Update()
        {
            if (!pointerHeld) return;
            if (!CanInvoke())
            {
                StopHolding();
                return;
            }
            if (repeater.Advance(Time.unscaledDeltaTime)) stepAction();
        }

        void OnDisable() => StopHolding();

        bool CanInvoke() => isActiveAndEnabled && button != null && button.interactable && stepAction != null;

        void StopHolding()
        {
            pointerHeld = false;
            repeater.Reset();
        }
    }
}
