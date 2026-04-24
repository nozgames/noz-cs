//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using NoZ.Platform;

namespace NoZ;

public struct TouchFinger
{
    public long Id;
    public Vector2 Position;
    public Vector2 StartPosition;
    public Vector2 Delta;
    public float Pressure;
    public float DownTime;
    public bool Active;
}

public static class Touch
{
    public const int MaxFingers = 10;

    private const float TapMaxDuration = 0.3f;
    private const float TapMaxDistance = 20f;
    private const float DoubleTapMaxInterval = 0.3f;
    private const float MultiFingerTapMaxDuration = 0.5f;
    private const float MultiFingerTapMaxDistance = 45f;
    private const float LongPressDuration = 0.5f;
    private const float DragThreshold = 20f;

    private enum MouseSimState { Idle, Pending, Dragging, DraggingWithSnap, Suppressed }

    private static readonly TouchFinger[] _fingers = new TouchFinger[MaxFingers];
    private static int _fingerCount;

    private static MouseSimState _mouseSimState;
    private static long _mouseSimulatedFingerId;
    private static Vector2 _mouseSimStartPosition;
    // Keeps IsSnapModifier true for the frame where the primary finger lifts
    // out of DraggingWithSnap, so the release-commit snaps the final position.
    private static bool _snapModifierLatched;

    private static bool _pinchActive;
    private static float _pinchScale = 1f;

    private static float _lastTapTime;
    private static Vector2 _lastTapPosition;
    private static bool _tapped;
    private static bool _doubleTapped;

    private static int _tapSessionMaxFingers;
    private static float _tapSessionStartTime;
    private static bool _tapSessionInvalid;
    private static bool _tapSessionFired;
    private static bool _twoFingerTapped;
    private static bool _threeFingerTapped;

    private static bool _twoFingerPanning;
    private static Vector2 _twoFingerCenter;
    private static Vector2 _twoFingerPrevCenter;
    private static Vector2 _twoFingerDelta;
    private static float _twoFingerDistance;
    private static float _twoFingerPrevDistance;
    private static float _twoFingerScale = 1f;
    private static float _twoFingerAngle;
    private static float _twoFingerPrevAngle;
    private static float _twoFingerRotation;

    public static bool SimulateMouse { get; set; }

    public static int FingerCount => _fingerCount;
    public static bool IsTouching => _fingerCount > 0;

    public static bool WasTapped => _tapped;
    public static bool WasDoubleTapped => _doubleTapped;
    public static bool WasTwoFingerTapped => _twoFingerTapped;
    public static bool WasThreeFingerTapped => _threeFingerTapped;
    public static Vector2 TapPosition => _lastTapPosition;

    public static bool IsPinching => _pinchActive;
    public static float PinchScale => _pinchScale;

    // A second finger landing during an active simulated drag is treated like Ctrl
    // (engage snap) rather than starting a two-finger pan/zoom/rotate.
    public static bool IsSnapModifier =>
        _mouseSimState == MouseSimState.DraggingWithSnap || _snapModifierLatched;

    public static bool IsTwoFingerPanning => _twoFingerPanning;
    public static Vector2 TwoFingerCenter => _twoFingerCenter;
    public static Vector2 TwoFingerDelta => _twoFingerDelta;
    public static float TwoFingerScale => _twoFingerScale;
    public static float TwoFingerRotation => _twoFingerRotation;

    public static ReadOnlySpan<TouchFinger> Fingers => _fingers.AsSpan(0, MaxFingers);

    public static TouchFinger GetFinger(int index) =>
        index >= 0 && index < MaxFingers ? _fingers[index] : default;

    public static void BeginFrame()
    {
        _tapped = false;
        _doubleTapped = false;
        _twoFingerTapped = false;
        _threeFingerTapped = false;
        _pinchScale = 1f;
        _twoFingerDelta = Vector2.Zero;
        _twoFingerScale = 1f;
        _twoFingerRotation = 0f;
        if (_mouseSimState != MouseSimState.DraggingWithSnap)
            _snapModifierLatched = false;

        for (var i = 0; i < MaxFingers; i++)
            _fingers[i].Delta = Vector2.Zero;

        CheckLongPress();
    }

    public static void ProcessEvent(PlatformEvent evt)
    {
        switch (evt.Type)
        {
            case PlatformEventType.TouchDown:   HandleTouchDown(evt);   break;
            case PlatformEventType.TouchUp:     HandleTouchUp(evt);     break;
            case PlatformEventType.TouchMove:   HandleTouchMove(evt);   break;
            case PlatformEventType.TouchCancel: HandleTouchCancel(evt); break;

            // SDL3 native pinch (trackpad). When fingers are on screen we drive
            // zoom from our own two-finger tracking instead, so ignore these.
            case PlatformEventType.PinchBegin:
                if (_fingerCount < 2) { _pinchActive = true; _pinchScale = 1f; }
                break;
            case PlatformEventType.PinchUpdate:
                if (_fingerCount < 2) _pinchScale = evt.PinchScale;
                break;
            case PlatformEventType.PinchEnd:
                if (_fingerCount < 2) { _pinchActive = false; _pinchScale = 1f; }
                break;
        }
    }

    private static void HandleTouchDown(in PlatformEvent evt)
    {
        var slot = FindOrAllocSlot(evt.FingerId);
        if (slot < 0) return;

        if (SimulateMouse)
        {
            if (_fingerCount == 0)
            {
                _mouseSimState = MouseSimState.Pending;
                _mouseSimulatedFingerId = evt.FingerId;
                _mouseSimStartPosition = evt.TouchPosition;
            }
            else if (_mouseSimState == MouseSimState.Dragging)
            {
                // Promote to snap modifier: keep the drag going, skip MouseUp,
                // and suppress two-finger pan/zoom/rotate below.
                _mouseSimState = MouseSimState.DraggingWithSnap;
            }
            else if (_mouseSimState != MouseSimState.Idle &&
                     _mouseSimState != MouseSimState.DraggingWithSnap)
            {
                _mouseSimState = MouseSimState.Suppressed;
            }
        }

        ref var f = ref _fingers[slot];
        f.Id = evt.FingerId;
        f.Position = evt.TouchPosition;
        f.StartPosition = evt.TouchPosition;
        f.Delta = Vector2.Zero;
        f.Pressure = evt.Pressure;
        f.DownTime = Time.TotalTime;
        f.Active = true;
        _fingerCount++;

        if (_fingerCount == 1)
        {
            _tapSessionStartTime = Time.TotalTime;
            _tapSessionMaxFingers = 1;
            _tapSessionInvalid = false;
            _tapSessionFired = false;
        }
        else if (_fingerCount > _tapSessionMaxFingers)
        {
            _tapSessionMaxFingers = _fingerCount;
        }

        if (_fingerCount >= 2 && _mouseSimState != MouseSimState.DraggingWithSnap)
        {
            _twoFingerPanning = true;
            RebaselineTwoFinger();
        }
    }

    private static void HandleTouchUp(in PlatformEvent evt)
    {
        var slot = FindSlot(evt.FingerId);
        if (slot < 0) return;

        ref var f = ref _fingers[slot];

        if (SimulateMouse && evt.FingerId == _mouseSimulatedFingerId)
        {
            switch (_mouseSimState)
            {
                case MouseSimState.Pending:
                    Input.ProcessEvent(PlatformEvent.MouseMove(f.Position));
                    Input.ProcessEvent(PlatformEvent.MouseDown(InputCode.MouseLeft));
                    Input.ProcessEvent(PlatformEvent.MouseUp(InputCode.MouseLeft));
                    break;
                case MouseSimState.Dragging:
                    Input.ProcessEvent(PlatformEvent.MouseMove(f.Position));
                    Input.ProcessEvent(PlatformEvent.MouseUp(InputCode.MouseLeft));
                    break;
                case MouseSimState.DraggingWithSnap:
                    Input.ProcessEvent(PlatformEvent.MouseMove(f.Position));
                    Input.ProcessEvent(PlatformEvent.MouseUp(InputCode.MouseLeft));
                    // Latch snap through this frame so the release-commit still
                    // sees IsSnapModifier == true; cleared next BeginFrame.
                    _snapModifierLatched = true;
                    break;
            }
            _mouseSimState = MouseSimState.Idle;
        }

        // Single-finger tap is gated on session max==1 so the tail of a pinch
        // or multi-finger gesture (last finger lifting) doesn't register as a tap.
        if (_fingerCount == 1 && _tapSessionMaxFingers == 1 &&
            Time.TotalTime - f.DownTime < TapMaxDuration &&
            Vector2.Distance(f.Position, f.StartPosition) < TapMaxDistance)
        {
            if (Time.TotalTime - _lastTapTime < DoubleTapMaxInterval &&
                Vector2.Distance(f.Position, _lastTapPosition) < TapMaxDistance)
                _doubleTapped = true;

            _tapped = true;
            _lastTapPosition = f.Position;
            _lastTapTime = Time.TotalTime;
        }
        // Multi-finger tap fires on the FIRST finger of the session lifting (not
        // the last). Eager firing keeps rapid successive taps independent —
        // otherwise the next tap's first finger would extend this session before
        // it resolved.
        else if (!_tapSessionFired && _tapSessionMaxFingers >= 2 && !_tapSessionInvalid &&
                 _fingerCount == _tapSessionMaxFingers &&
                 Time.TotalTime - _tapSessionStartTime < MultiFingerTapMaxDuration)
        {
            _tapSessionFired = true;
            if (_tapSessionMaxFingers == 2) _twoFingerTapped = true;
            else if (_tapSessionMaxFingers == 3) _threeFingerTapped = true;
        }

        f = default;
        _fingerCount = Math.Max(0, _fingerCount - 1);

        // A non-primary finger released while snap-dragging leaves only the
        // primary finger; drop back to plain Dragging so snap disengages.
        if (_mouseSimState == MouseSimState.DraggingWithSnap && _fingerCount == 1)
            _mouseSimState = MouseSimState.Dragging;

        if (_fingerCount < 2)
            _twoFingerPanning = false;
        else if (_mouseSimState != MouseSimState.DraggingWithSnap)
            RebaselineTwoFinger();
    }

    private static void HandleTouchMove(in PlatformEvent evt)
    {
        var slot = FindSlot(evt.FingerId);
        if (slot < 0) return;

        ref var f = ref _fingers[slot];
        f.Delta = evt.TouchDelta;
        f.Position = evt.TouchPosition;
        f.Pressure = evt.Pressure;

        if (!_tapSessionInvalid &&
            Vector2.Distance(f.Position, f.StartPosition) > MultiFingerTapMaxDistance)
            _tapSessionInvalid = true;

        if (SimulateMouse && evt.FingerId == _mouseSimulatedFingerId)
        {
            if (_mouseSimState == MouseSimState.Pending &&
                Vector2.Distance(evt.TouchPosition, _mouseSimStartPosition) > DragThreshold)
            {
                _mouseSimState = MouseSimState.Dragging;
                Input.ProcessEvent(PlatformEvent.MouseMove(_mouseSimStartPosition));
                Input.ProcessEvent(PlatformEvent.MouseDown(InputCode.MouseLeft));
                Input.ProcessEvent(PlatformEvent.MouseMove(evt.TouchPosition));
            }
            else if (_mouseSimState == MouseSimState.Dragging ||
                     _mouseSimState == MouseSimState.DraggingWithSnap)
            {
                Input.ProcessEvent(PlatformEvent.MouseMove(evt.TouchPosition));
            }
        }

        if (_twoFingerPanning)
        {
            _twoFingerPrevCenter = _twoFingerCenter;
            _twoFingerCenter = ComputeTwoFingerCenter();
            _twoFingerDelta += _twoFingerCenter - _twoFingerPrevCenter;

            _twoFingerPrevDistance = _twoFingerDistance;
            _twoFingerDistance = ComputeTwoFingerDistance();
            if (_twoFingerPrevDistance >= 1f)
                _twoFingerScale *= _twoFingerDistance / _twoFingerPrevDistance;

            _twoFingerPrevAngle = _twoFingerAngle;
            _twoFingerAngle = ComputeTwoFingerAngle();
            var rotDelta = _twoFingerAngle - _twoFingerPrevAngle;
            if (rotDelta > MathF.PI) rotDelta -= MathF.Tau;
            else if (rotDelta < -MathF.PI) rotDelta += MathF.Tau;
            _twoFingerRotation += rotDelta;
        }
    }

    private static void HandleTouchCancel(in PlatformEvent evt)
    {
        var slot = FindSlot(evt.FingerId);
        if (slot < 0) return;

        if (SimulateMouse && evt.FingerId == _mouseSimulatedFingerId)
        {
            if (_mouseSimState == MouseSimState.Dragging ||
                _mouseSimState == MouseSimState.DraggingWithSnap)
                Input.ProcessEvent(PlatformEvent.MouseUp(InputCode.MouseLeft));
            _mouseSimState = MouseSimState.Idle;
        }

        _fingers[slot] = default;
        _fingerCount = Math.Max(0, _fingerCount - 1);
        _tapSessionInvalid = true;

        if (_mouseSimState == MouseSimState.DraggingWithSnap && _fingerCount == 1)
            _mouseSimState = MouseSimState.Dragging;

        if (_fingerCount < 2)
            _twoFingerPanning = false;
        else if (_mouseSimState != MouseSimState.DraggingWithSnap)
            RebaselineTwoFinger();
    }

    private static void CheckLongPress()
    {
        if (!SimulateMouse || _mouseSimState != MouseSimState.Pending) return;

        var slot = FindSlot(_mouseSimulatedFingerId);
        if (slot < 0) return;

        ref readonly var f = ref _fingers[slot];
        if (Time.TotalTime - f.DownTime < LongPressDuration) return;

        _mouseSimState = MouseSimState.Suppressed;
        Input.ProcessEvent(PlatformEvent.MouseMove(f.Position));
        Input.ProcessEvent(PlatformEvent.MouseDown(InputCode.MouseRight));
        Input.ProcessEvent(PlatformEvent.MouseUp(InputCode.MouseRight));
    }

    private static void RebaselineTwoFinger()
    {
        _twoFingerCenter = ComputeTwoFingerCenter();
        _twoFingerPrevCenter = _twoFingerCenter;
        _twoFingerDistance = ComputeTwoFingerDistance();
        _twoFingerPrevDistance = _twoFingerDistance;
        _twoFingerAngle = ComputeTwoFingerAngle();
        _twoFingerPrevAngle = _twoFingerAngle;
    }

    private static int GetFirstTwoFingers(out Vector2 a, out Vector2 b)
    {
        a = b = Vector2.Zero;
        var count = 0;
        for (var i = 0; i < MaxFingers; i++)
        {
            if (!_fingers[i].Active) continue;
            if (count == 0) a = _fingers[i].Position;
            else { b = _fingers[i].Position; return 2; }
            count++;
        }
        return count;
    }

    private static Vector2 ComputeTwoFingerCenter()
    {
        var count = GetFirstTwoFingers(out var a, out var b);
        return count switch
        {
            2 => (a + b) * 0.5f,
            1 => a,
            _ => Vector2.Zero,
        };
    }

    private static float ComputeTwoFingerDistance() =>
        GetFirstTwoFingers(out var a, out var b) == 2 ? Vector2.Distance(a, b) : 0f;

    private static float ComputeTwoFingerAngle() =>
        GetFirstTwoFingers(out var a, out var b) == 2 ? MathF.Atan2(b.Y - a.Y, b.X - a.X) : 0f;

    private static int FindSlot(long fingerId)
    {
        for (var i = 0; i < MaxFingers; i++)
            if (_fingers[i].Active && _fingers[i].Id == fingerId) return i;
        return -1;
    }

    private static int FindOrAllocSlot(long fingerId)
    {
        var existing = FindSlot(fingerId);
        if (existing >= 0) return existing;

        for (var i = 0; i < MaxFingers; i++)
            if (!_fingers[i].Active) return i;
        return -1;
    }
}
