using System.Runtime.CompilerServices;
using UnityEngine;

namespace VaporStateMachine
{

    public interface ITimer
    {
        double StartTime { get; }
        double Elapsed { get; }
    }

    public struct FiniteTimer : ITimer
    {
        public double StartTime { get; private set; }
        public double Elapsed => Time.timeAsDouble - StartTime;

        public FiniteTimer(double startTime)
        {
            StartTime = startTime;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FiniteTimer Reset() => new(Time.timeAsDouble);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(FiniteTimer timer, float duration) => timer.Elapsed > duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(FiniteTimer timer, float duration) => timer.Elapsed < duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(FiniteTimer timer, float duration) => timer.Elapsed >= duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(FiniteTimer timer, float duration) => timer.Elapsed <= duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(FiniteTimer timer, double duration) => timer.Elapsed > duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(FiniteTimer timer, double duration) => timer.Elapsed < duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(FiniteTimer timer, double duration) => timer.Elapsed >= duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(FiniteTimer timer, double duration) => timer.Elapsed <= duration;
    }

    public class Timer : ITimer
    {
        public double StartTime { get; private set; }
        public double Elapsed => Time.timeAsDouble - StartTime;

        public Timer()
        {
            StartTime = Time.timeAsDouble;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Pause(float deltaTime)
        {
            StartTime += deltaTime;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset() => StartTime = Time.timeAsDouble;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(Timer timer, float duration) => timer.Elapsed > duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(Timer timer, float duration) => timer.Elapsed < duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(Timer timer, float duration) => timer.Elapsed >= duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(Timer timer, float duration) => timer.Elapsed <= duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(Timer timer, double duration) => timer.Elapsed > duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(Timer timer, double duration) => timer.Elapsed < duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(Timer timer, double duration) => timer.Elapsed >= duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(Timer timer, double duration) => timer.Elapsed <= duration;
    }
}
