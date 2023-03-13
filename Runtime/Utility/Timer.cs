using System.Runtime.CompilerServices;
using UnityEngine;

namespace VaporStateMachine
{
    public class Timer
    {
        public float StartTime;
        public float Elapsed => Time.time - StartTime;

        public Timer()
        {
            StartTime = Time.time;
        }

        public void Reset()
        {
            StartTime = Time.time;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(Timer timer, float duration)
            => timer.Elapsed > duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(Timer timer, float duration)
            => timer.Elapsed < duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(Timer timer, float duration)
            => timer.Elapsed >= duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(Timer timer, float duration)
            => timer.Elapsed <= duration;
    }
}
