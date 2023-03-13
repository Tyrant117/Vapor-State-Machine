using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VaporStateMachine
{
    public interface IStateMachine
    {
        /// <summary>
		/// Tells the state machine that, if there is a state transition pending,
		/// now is the time to perform it.
		/// </summary>
		void StateCanExit();

        void RequestStateChange(int name, bool force = false);
        void RequestStateChange(int layer, int name, bool force = false);
    }
}
