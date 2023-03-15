using System.Collections;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace VaporStateMachine
{
    public class StateMachine : State, IStateMachine
    {
        static readonly ProfilerMarker s_EnterMarker = new(ProfilerCategory.Scripts, "Vapor.StateMachine.Enter");
        static readonly ProfilerMarker s_UpdateMarker = new(ProfilerCategory.Scripts, "Vapor.StateMachine.Update");
        static readonly ProfilerMarker s_ExitMarker = new(ProfilerCategory.Scripts, "Vapor.StateMachine.Exit");
        static readonly ProfilerMarker s_ChangeStateMarker = new(ProfilerCategory.Scripts, "Vapor.StateMachine.ChangeState");

        /// <summary>
		/// A bundle of a state together with the outgoing transitions and trigger transitions.
		/// It's useful, as you only need to do one Dictionary lookup for these three items.
		/// => Much better performance
		/// </summary>
		private class StateBundle
        {
            // By default, these fields are all null and only get a value when you need them
            // => Lazy evaluation => Memory efficient, when you only need a subset of features
            public State state;
            public List<Transition> transitions;
            public Dictionary<int, List<Transition>> triggerToTransitions;

            public void AddTransition(Transition t)
            {
                transitions ??= new List<Transition>();
                transitions.Add(t);
            }

            public void AddTriggerTransition(int trigger, Transition transition)
            {
                triggerToTransitions ??= new Dictionary<int, List<Transition>>();

                if (!triggerToTransitions.TryGetValue(trigger, out List<Transition> transitionsOfTrigger))
                {
                    transitionsOfTrigger = new List<Transition>();
                    triggerToTransitions.Add(trigger, transitionsOfTrigger);
                }
                transitionsOfTrigger.Add(transition);
            }
        }


        private State activeState = null;
        public State ActiveState
        {
            get
            {
                EnsureIsInitializedFor();
                return activeState;
            }
        }
        public int ActiveStateID => ActiveState.ID;
        public string ActiveStateName => ActiveState.Name;
        public bool IsRoot => StateMachine == null;

        private (int state, bool hasState) startState = (EMPTY_STATE, false);
        private (int state, bool isPending) pendingState = (EMPTY_STATE, false);

        // A cached empty list of transitions (For improved readability, less GC)
        private static readonly List<Transition> noTransitions = new(0);
        private static readonly Dictionary<int, List<Transition>> noTriggerTransitions = new(0);

        // Central storage of states
        private readonly Dictionary<int, StateBundle> nameToStateBundle = new();
        private readonly Dictionary<int, string> stateToStringMap = new();

        private List<Transition> activeTransitions = noTransitions;
        private Dictionary<int, List<Transition>> activeTriggerTransitions = noTriggerTransitions;

        private readonly List<Transition> transitionsFromAny = new();
        private readonly Dictionary<int, List<Transition>> triggerTransitionsFromAny = new();

        #region - Initialization -
        /// <summary>
		/// Initialises a new instance of the StateMachine class
		/// </summary>
		/// <param name="needsExitTime">(Only for hierarchical states):
		/// 	Determins whether the state machine as a state of a parent state machine is allowed to instantly
		/// 	exit on a transition (false), or if it should wait until the active state is ready for a
		/// 	state change (true).</param>
		public StateMachine(string name, bool canExitInstantly = false) : base(name, canExitInstantly)
        {

        }

        /// <summary>
        /// Throws an exception if the state machine is not initialised yet.
        /// </summary>
        /// <param name="context">String message for which action the fsm should
        /// 	be initialised for.</param>
        private void EnsureIsInitializedFor()
        {
            if (activeState == null)
            {
                Debug.LogError(StateMachineExceptions.StateMachineNotInitialized);
            }
        }

        /// <summary>
        /// Defines the entry point of the state machine
        /// </summary>
        /// <param name="name">The name / identifier of the start state</param>
        public void SetDefaultState(int name)
        {
            startState = (name, true);
        }

        /// <summary>
        /// Defines the entry point of the state machine
        /// </summary>
        /// <param name="name">The name / identifier of the start state</param>
        public void SetDefaultState(State state)
        {
            startState = (state.ID, true);
        }

        /// <summary>
		/// Calls OnEnter if it is the root machine, therefore initialising the state machine
		/// </summary>
		public override void Init()
        {
            if (!IsRoot) return;

            OnEnter();
        }
        #endregion

        #region - State Management -
        /// <summary>
		/// Initialises the state machine and must be called before OnLogic is called.
		/// It sets the activeState to the selected startState.
		/// </summary>
		public override void OnEnter()
        {
            s_EnterMarker.Begin();
            if (!startState.hasState)
            {
                Debug.LogError(StateMachineExceptions.NoDefaultStateFound);
            }

            ChangeState(startState.state);

            foreach (var t in transitionsFromAny)
            {
                t.OnEnter();
            }

            foreach (var transitions in triggerTransitionsFromAny.Values)
            {
                foreach (var t in transitions)
                {
                    t.OnEnter();
                }
            }
            s_EnterMarker.End();
        }

        /// <summary>
		/// Runs one logic step. It does at most one transition itself and
		/// calls the active state's logic function (after the state transition, if
		/// one occurred).
		/// </summary>
		public override void OnUpdate()
        {
            s_UpdateMarker.Begin();
            EnsureIsInitializedFor();

            if (!TryAllGlobalTransitions())
            {
                TryAllDirectTransitions();
            }

            activeState.OnUpdate();
            s_UpdateMarker.End();
        }

        public override void OnExit()
        {
            s_ExitMarker.Begin();
            if (activeState != null)
            {
                activeState.OnExit();
                // By setting the activeState to null, the state's onExit method won't be called
                // a second time when the state machine enters again (and changes to the start state)
                activeState = null;
            }
            s_ExitMarker.End();
        }

        /// <summary>
        /// Notifies the state machine that the state can cleanly exit,
        /// and if a state change is pending, it will execute it.
        /// </summary>
        public void StateCanExit()
        {
            if (pendingState.isPending)
            {
                int state = pendingState.state;
                // When the pending state is a ghost state, ChangeState() will have
                // to try all outgoing transitions, which may overwrite the pendingState.
                // That's why it is first cleared, and not afterwards, as that would overwrite
                // a new, valid pending state.
                pendingState = (EMPTY_STATE, false);
                ChangeState(state);
            }

            StateMachine?.StateCanExit();
        }

        public override void OnExitRequest()
        {
            StateMachine?.StateCanExit();
        }

        /// <summary>
		/// Instantly changes to the target state
		/// </summary>
		/// <param name="name">The name / identifier of the active state</param>
		private void ChangeState(int name)
        {
            s_ChangeStateMarker.Begin();
            activeState?.OnExit();

            if (!nameToStateBundle.TryGetValue(name, out StateBundle bundle) || bundle.state == null)
            {
                if (stateToStringMap.TryGetValue(name, out string nameString))
                {
                    Debug.LogError(StateMachineExceptions.StateNotFound(nameString));
                }
                else
                {
                    Debug.LogError(StateMachineExceptions.StateNotFound(name.ToString()));
                }
            }

            activeTransitions = bundle.transitions ?? noTransitions;
            activeTriggerTransitions = bundle.triggerToTransitions ?? noTriggerTransitions;

            activeState = bundle.state;
            activeState.OnEnter();

            foreach (Transition t in activeTransitions)
            {
                t.OnEnter();
            }

            foreach (List<Transition> transitions in activeTriggerTransitions.Values)
            {
                foreach (Transition t in transitions)
                {
                    t.OnEnter();
                }
            }

            if (activeState.CanExitInstantly)
            {
                TryAllDirectTransitions();
            }
            s_ChangeStateMarker.End();
        }

        /// <summary>
		/// Requests a state change, respecting the <c>needsExitTime</c> property of the active state
		/// </summary>
		/// <param name="name">The name / identifier of the target state</param>
		/// <param name="forceInstantly">Overrides the needsExitTime of the active state if true,
		/// therefore forcing an immediate state change</param>
		public void RequestStateChange(int name, bool force = false)
        {
            if (force)
            {
                ChangeState(name);
            }
            else
            {
                pendingState = (name, true);
                activeState.OnExitRequest();
                /**
				 * If it can exit, the activeState would call
				 * -> state.fsm.StateCanExit() which in turn would call
				 * -> fsm.ChangeState(...)
				 */
            }
        }

        /// <summary>
        /// Requests a state change, respecting the <c>needsExitTime</c> property of the active state
        /// </summary>
        /// <param name="name">The name / identifier of the target state</param>
        /// <param name="forceInstantly">Overrides the needsExitTime of the active state if true,
        /// therefore forcing an immediate state change</param>
        public void RequestStateChange(int layer, int name, bool force = false)
        {
            if (force)
            {
                ChangeState(name);
            }
            else
            {
                pendingState = (name, true);
                activeState.OnExitRequest();
                /**
				 * If it can exit, the activeState would call
				 * -> state.fsm.StateCanExit() which in turn would call
				 * -> fsm.ChangeState(...)
				 */
            }
        }
        #endregion

        #region - State Machine Management -
        /// <summary>
		/// Gets the StateBundle belonging to the <c>name</c> state "slot" if it exists.
		/// Otherwise it will create a new StateBundle, that will be added to the Dictionary,
		/// and return the newly created instance.
		/// </summary>
		/// <param name="name"></param>
		/// <returns></returns>
		private StateBundle GetOrCreateStateBundle(int name)
        {
            if (!nameToStateBundle.TryGetValue(name, out StateBundle bundle))
            {
                bundle = new StateBundle();
                nameToStateBundle.Add(name, bundle);
            }

            return bundle;
        }

        /// <summary>
		/// Adds a new node / state to the state machine.
		/// </summary>
		/// <param name="name">The name / identifier of the new state</param>
		/// <param name="state">The new state instance, e.g. <c>State</c>, <c>CoState</c>, <c>StateMachine</c></param>
		public void AddState(State state)
        {
            state.StateMachine = this;
            state.Init();

            StateBundle bundle = GetOrCreateStateBundle(state.ID);
            bundle.state = state;
            stateToStringMap.Add(state.ID, state.Name);

            if (nameToStateBundle.Count == 1 && !startState.hasState)
            {
                SetDefaultState(state.ID);
            }
        }

        public T GetState<T>(string name) where T : State
        {
            if (!nameToStateBundle.TryGetValue(name.GetHashCode(), out StateBundle bundle) || bundle.state == null)
            {
                Debug.LogError(StateMachineExceptions.StateNotFound(name));
            }
            return bundle.state as T;
        }

        public T GetState<T>(int name) where T : State
        {
            if (!nameToStateBundle.TryGetValue(name.GetHashCode(), out StateBundle bundle) || bundle.state == null)
            {
                if (stateToStringMap.TryGetValue(name, out string nameString))
                {
                    Debug.LogError(StateMachineExceptions.StateNotFound(nameString));
                }
                else
                {
                    Debug.LogError(StateMachineExceptions.StateNotFound(name.ToString()));
                }
            }
            return bundle.state as T;
        }

        public T GetSubStateMachine<T>(string name) where T : State, IStateMachine
        {
            return GetState<T>(name);
        }

        public T GetSubStateMachine<T>(int name) where T : State, IStateMachine
        {
            return GetState<T>(name);
        }
        #endregion

        #region - Transitions -
        /// <summary>
		/// Initialises a transition, i.e. sets its fsm attribute, and then calls its Init method.
		/// </summary>
		/// <param name="transition"></param>
		private void InitTransition(Transition transition)
        {
            transition.StateMachine = this;
            transition.Init();
        }

        /// <summary>
        /// Adds a new transition between two states.
        /// </summary>
        /// <param name="transition">The transition instance</param>
        public void AddTransition(Transition transition)
        {
            InitTransition(transition);

            StateBundle bundle = GetOrCreateStateBundle(transition.From);
            bundle.AddTransition(transition);
        }

        /// <summary>
		/// Adds a new transition that can happen from any possible state
		/// </summary>
		/// <param name="transition">The transition instance; The "from" field can be
		/// left empty, as it has no meaning in this context.</param>
		public void AddTransitionFromAny(Transition transition)
        {
            InitTransition(transition);

            transitionsFromAny.Add(transition);
        }

        /// <summary>
		/// Adds a new trigger transition between two states that is only checked
		/// when the specified trigger is activated.
		/// </summary>
		/// <param name="trigger">The name / identifier of the trigger</param>
		/// <param name="transition">The transition instance, e.g. Transition, TransitionAfter, ...</param>
		public void AddTriggerTransition(int trigger, Transition transition)
        {
            InitTransition(transition);

            StateBundle bundle = GetOrCreateStateBundle(transition.From);
            bundle.AddTriggerTransition(trigger, transition);
        }

        /// <summary>
		/// Adds a new trigger transition that can happen from any possible state, but is only
		/// checked when the specified trigger is activated.
		/// </summary>
		/// <param name="trigger">The name / identifier of the trigger</param>
		/// <param name="transition">The transition instance; The "from" field can be
		/// left empty, as it has no meaning in this context.</param>
		public void AddTriggerTransitionFromAny(int trigger, Transition transition)
        {
            InitTransition(transition);

            if (!triggerTransitionsFromAny.TryGetValue(trigger, out List<Transition> transitionsOfTrigger))
            {
                transitionsOfTrigger = new List<Transition>();
                triggerTransitionsFromAny.Add(trigger, transitionsOfTrigger);
            }

            transitionsOfTrigger.Add(transition);
        }

        /// <summary>
		/// Adds two transitions:
		/// If the condition of the transition instance is true, it transitions from the "from"
		/// state to the "to" state. Otherwise it performs a transition in the opposite direction,
		/// i.e. from "to" to "from".
		/// </summary>
		/// <remarks>
		/// Internally the same transition instance will be used for both transitions
		/// by wrapping it in a ReverseTransition.
		/// </remarks>
		public void AddTwoWayTransition(Transition transition)
        {
            InitTransition(transition);
            AddTransition(transition);

            Transition reverse = transition.Reverse();
            InitTransition(reverse);
            AddTransition(reverse);
        }

        /// <summary>
		/// Adds two transitions that are only checked when the specified trigger is activated:
		/// If the condition of the transition instance is true, it transitions from the "from"
		/// state to the "to" state. Otherwise it performs a transition in the opposite direction,
		/// i.e. from "to" to "from".
		/// </summary>
		/// <remarks>
		/// Internally the same transition instance will be used for both transitions
		/// by wrapping it in a ReverseTransition.
		/// </remarks>
		public void AddTwoWayTriggerTransition(int trigger, Transition transition)
        {
            InitTransition(transition);
            AddTriggerTransition(trigger, transition);

            Transition reverse = transition.Reverse();
            InitTransition(reverse);
            AddTriggerTransition(trigger, reverse);
        }

        /// <summary>
		/// Checks if a transition can take place, and if this is the case, transition to the
		/// "to" state and return true. Otherwise it returns false.
		/// </summary>
		/// <param name="transition"></param>
		/// <returns></returns>
		private int TryTransition(Transition transition)
        {
            return !transition.ShouldTransition() ? 0 : transition.Desire;
        }

        /// <summary>
		/// Tries the "normal" transitions that transition from one specific state to another.
		/// </summary>
		/// <returns>Returns true if a transition occurred.</returns>
		private bool TryAllDirectTransitions()
        {
            if (DetermineTransition(activeTransitions, out var to))
            {
                RequestStateChange(to);
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
		/// Tries the "global" transitions that can transition from any state
		/// </summary>
		/// <returns>Returns true if a transition occurred.</returns>
		private bool TryAllGlobalTransitions()
        {
            if (DetermineTransition(transitionsFromAny, out var to))
            {
                RequestStateChange(to);
                return true;
            }
            else
            {
                return false;
            }
        }

        private bool DetermineTransition(List<Transition> transitions, out int ToState)
        {
            int desire = 0;
            ToState = EMPTY_STATE;
            foreach (Transition transition in transitions)
            {
                // Don't transition to the "to" state, if that state is already the active state
                if (transition.To == activeState.ID)
                {
                    continue;
                }

                int d = TryTransition(transition);
                if (d > desire)
                {
                    ToState = transition.To;
                }
            }
            return ToState != EMPTY_STATE;
        }
        #endregion

        #region - Trigger Transitions -
        /// <summary>
		/// Activates the specified trigger, checking all targeted trigger transitions to see whether
		/// a transition should occur.
		/// </summary>
		/// <param name="trigger">The name / identifier of the trigger</param>
		/// <returns>True when a transition occurred, otherwise false</returns>
		private bool TryTrigger(int trigger)
        {
            EnsureIsInitializedFor();

            if (triggerTransitionsFromAny.TryGetValue(trigger, out List<Transition> triggerTransitions))
            {
                if (DetermineTransition(triggerTransitions, out var to))
                {
                    RequestStateChange(to);
                    return true;
                }
            }

            if (activeTriggerTransitions.TryGetValue(trigger, out triggerTransitions))
            {
                if (DetermineTransition(triggerTransitions, out var to))
                {
                    RequestStateChange(to);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
		/// Activates the specified trigger in all active states of the hierarchy, checking all targeted
		/// trigger transitions to see whether a transition should occur.
		/// </summary>
		/// <param name="trigger">The name / identifier of the trigger</param>
		public void Trigger(int trigger)
        {
            // If a transition occurs, then the trigger should not be activated
            // in the new active state, that the state machine just switched to.
            if (TryTrigger(trigger))
            {
                return;
            }
        }
        #endregion

        #region - Actions -
        /// <summary>
		/// Runs an action on the currently active state.
		/// </summary>
		/// <param name="trigger">Name of the action</param>
		public void OnInvokeAction(int actionID)
        {
            EnsureIsInitializedFor();
            activeState?.OnAction(actionID);
        }

        /// <summary>
        /// Runs an action on the currently active state and lets you pass one data parameter.
        /// </summary>
        /// <param name="trigger">Name of the action</param>
        /// <param name="data">Any custom data for the parameter</param>
        /// <typeparam name="TData">Type of the data parameter.
        /// 	Should match the data type of the action that was added via AddAction<T>(...).</typeparam>
        public void OnInvokeAction<TData>(int actionID, TData data)
        {
            EnsureIsInitializedFor();
            activeState?.OnAction(actionID, data);
        }
        #endregion

        #region - Pooling -
        public override void RemoveFromPool()
        {

        }

        public override void OnReturnedToPool()
        {

        }
        #endregion
    }
}
