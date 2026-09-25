using UnityEngine;

namespace ProjectLEA.Manuel.Managers
{
    /// <summary>
    /// Keeps <see cref="Time.timeScale"/> honest when more than one full-screen UI can hold the
    /// match still.
    ///
    /// The shop and the class select screen both freeze the match while they are up, and they
    /// hand off to each other at a phase change: the select screen closes on the very frame the
    /// buy phase opens the shop. If each captured and restored the time scale itself, whichever
    /// one opened that frame would capture the 0 the other had not restored yet, and the match
    /// would stay frozen after the shop later closed. A single depth counter makes the nesting
    /// correct regardless of which UI ran first.
    ///
    /// Everything driven off <see cref="Time.unscaledTime"/> - the phase timers included - keeps
    /// running while paused, which is exactly why a buy countdown does not stop because the
    /// shop is open.
    /// </summary>
    public static class MatchUiPause
    {
        private static int _depth;
        private static float _savedTimeScale = 1f;

        /// <summary>True while any full-screen UI holds the match still.</summary>
        public static bool IsPaused => _depth > 0;

        /// <summary>
        /// A UI took the screen. The first one in remembers what the time scale was, and every
        /// one after just notes that there is another UI on the stack.
        /// </summary>
        public static void Push()
        {
            if (_depth == 0) _savedTimeScale = Time.timeScale;

            _depth++;
            Time.timeScale = 0f;
        }

        /// <summary>
        /// A UI left the screen. Only the last one out restores the time scale, so a UI that
        /// closes while another is still up leaves the match frozen for the one that remains.
        /// </summary>
        public static void Pop()
        {
            if (_depth == 0) return;

            _depth--;
            if (_depth == 0) Time.timeScale = _savedTimeScale;
        }
    }
}
