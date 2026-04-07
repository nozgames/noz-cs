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
    public bool Active;
}

public static class Touch
{
    public const int MaxFingers = 10;

    private static readonly TouchFinger[] _fingers = new TouchFinger[MaxFingers];
    private static int _fingerCount;

    // Pinch gesture (driven by SDL native events)
    private static float _pinchScale;
    private static bool _pinchActive;

    // Tap detection
    private static float _lastTapTime;
    private static Vector2 _lastTapPosition;
    private static bool _tapped;
    private static bool _doubleTapped;
    private const float TapMaxDuration = 0.3f;
    private const float TapMaxDistance = 20f;
    private const float DoubleTapMaxInterval = 0.3f;

    // Two-finger pan gesture
    private static bool _twoFingerPanning;
    private static Vector2 _twoFingerCenter;
    private static Vector2 _twoFingerPrevCenter;
    private static Vector2 _twoFingerDelta;

    // Track finger down times for tap detection
    private static readonly float[] _fingerDownTimes = new float[MaxFingers];

    public static int FingerCount => _fingerCount;
    public static bool IsTouching => _fingerCount > 0;
    public static bool WasTapped => _tapped;
    public static bool WasDoubleTapped => _doubleTapped;
    public static Vector2 TapPosition => _lastTapPosition;

    // Pinch gesture
    public static bool IsPinching => _pinchActive;
    public static float PinchScale => _pinchScale;

    // Two-finger pan gesture
    public static bool IsTwoFingerPanning => _twoFingerPanning;
    public static Vector2 TwoFingerCenter => _twoFingerCenter;
    public static Vector2 TwoFingerDelta => _twoFingerDelta;

    public static ReadOnlySpan<TouchFinger> Fingers => _fingers.AsSpan(0, MaxFingers);

    public static TouchFinger GetFinger(int index) =>
        index >= 0 && index < MaxFingers ? _fingers[index] : default;

    public static void BeginFrame()
    {
        _tapped = false;
        _doubleTapped = false;
        _pinchScale = 1f;
        _twoFingerDelta = Vector2.Zero;

        for (var i = 0; i < MaxFingers; i++)
            _fingers[i].Delta = Vector2.Zero;
    }

    public static void ProcessEvent(PlatformEvent evt)
    {
        switch (evt.Type)
        {
            case PlatformEventType.TouchDown:
            {
                var slot = FindOrAllocSlot(evt.FingerId);
                if (slot < 0) break;

                ref var f = ref _fingers[slot];
                f.Id = evt.FingerId;
                f.Position = evt.TouchPosition;
                f.StartPosition = evt.TouchPosition;
                f.Delta = Vector2.Zero;
                f.Pressure = evt.Pressure;
                f.Active = true;
                _fingerDownTimes[slot] = Time.TotalTime;
                _fingerCount++;

                if (_fingerCount >= 2 && !_twoFingerPanning)
                {
                    _twoFingerPanning = true;
                    _twoFingerCenter = ComputeTwoFingerCenter();
                    _twoFingerPrevCenter = _twoFingerCenter;
                }
                break;
            }

            case PlatformEventType.TouchUp:
            {
                var slot = FindSlot(evt.FingerId);
                if (slot < 0) break;

                ref var f = ref _fingers[slot];
                var duration = Time.TotalTime - _fingerDownTimes[slot];
                var distance = Vector2.Distance(f.Position, f.StartPosition);

                // Detect tap: short duration, minimal movement, single finger
                if (_fingerCount == 1 && duration < TapMaxDuration && distance < TapMaxDistance)
                {
                    var timeSinceLastTap = Time.TotalTime - _lastTapTime;
                    var distFromLastTap = Vector2.Distance(f.Position, _lastTapPosition);

                    if (timeSinceLastTap < DoubleTapMaxInterval && distFromLastTap < TapMaxDistance)
                        _doubleTapped = true;

                    _tapped = true;
                    _lastTapPosition = f.Position;
                    _lastTapTime = Time.TotalTime;
                }

                f = default;
                _fingerCount = Math.Max(0, _fingerCount - 1);

                if (_fingerCount < 2)
                    _twoFingerPanning = false;
                break;
            }

            case PlatformEventType.TouchMove:
            {
                var slot = FindSlot(evt.FingerId);
                if (slot < 0) break;

                ref var f = ref _fingers[slot];
                f.Delta = evt.TouchDelta;
                f.Position = evt.TouchPosition;
                f.Pressure = evt.Pressure;

                if (_twoFingerPanning)
                {
                    _twoFingerPrevCenter = _twoFingerCenter;
                    _twoFingerCenter = ComputeTwoFingerCenter();
                    _twoFingerDelta += _twoFingerCenter - _twoFingerPrevCenter;
                }
                break;
            }

            case PlatformEventType.TouchCancel:
            {
                var slot = FindSlot(evt.FingerId);
                if (slot < 0) break;

                _fingers[slot] = default;
                _fingerCount = Math.Max(0, _fingerCount - 1);

                if (_fingerCount < 2)
                    _twoFingerPanning = false;
                break;
            }

            // SDL3 native pinch gesture
            case PlatformEventType.PinchBegin:
                _pinchActive = true;
                _pinchScale = 1f;
                break;

            case PlatformEventType.PinchUpdate:
                _pinchScale = evt.PinchScale;
                break;

            case PlatformEventType.PinchEnd:
                _pinchActive = false;
                _pinchScale = 1f;
                break;
        }
    }

    private static Vector2 ComputeTwoFingerCenter()
    {
        var count = 0;
        var center = Vector2.Zero;
        for (var i = 0; i < MaxFingers && count < 2; i++)
        {
            if (!_fingers[i].Active) continue;
            center += _fingers[i].Position;
            count++;
        }
        return count > 0 ? center / count : Vector2.Zero;
    }

    private static int FindSlot(long fingerId)
    {
        for (var i = 0; i < MaxFingers; i++)
        {
            if (_fingers[i].Active && _fingers[i].Id == fingerId)
                return i;
        }
        return -1;
    }

    private static int FindOrAllocSlot(long fingerId)
    {
        var existing = FindSlot(fingerId);
        if (existing >= 0) return existing;

        for (var i = 0; i < MaxFingers; i++)
        {
            if (!_fingers[i].Active)
                return i;
        }
        return -1;
    }
}
