using JetBrains.Annotations;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VaporStateMachine
{
    public class LayeredStateMachine : State, IStateMachine
    {
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


        private Dictionary<int, State> activeStates = new();
        public Dictionary<int, State> ActiveStates
        {
            get
            {
                EnsureIsInitializedFor();
                return activeStates;
            }
        }
        private readonly List<int> _activeStateIDs = new();
        public List<int> GetActiveStateIDs()
        {
            _activeStateIDs.Clear();
            for (int i = 0; i < activeStates.Count; i++)
            {
                _activeStateIDs.Add(activeStates[i].ID);
            }
            return _activeStateIDs;
        }
        private readonly List<string> _activeStateNames = new();
        public List<string> GetActiveStateNames()
        {
            _activeStateNames.Clear();
            for (int i = 0; i < activeStates.Count; i++)
            {
                _activeStateNames.Add(activeStates[i].Name);
            }
            return _activeStateNames;
        }
        
        public bool IsRoot => StateMachine == null;

        private Dictionary<int, (int layer, int state, bool hasState)> startStates = new();
        private Dictionary<int, (int layer, int state, bool isPending)> pendingStates = new();

        // A cached empty list of transitions (For improved readability, less GC)
        private static readonly List<Transition> noTransitions = new(0);
        private static readonly Dictionary<int, List<Transition>> noTriggerTransitions = new(0);

        // Central storage of states
        private readonly Dictionary<Vector2Int, StateBundle> nameToStateBundle = new();
        private readonly Dictionary<Vector2Int, string> stateToStringMap = new();

        private Dictionary<int, List<Transition>> activeTransitions = new();
        private Dictionary<int, Dictionary<int, List<Transition>>> activeTriggerTransitions = new();

        private readonly Dictionary<int, List<Transition>> transitionsFromAny = new();
        private readonly Dictionary<int, Dictionary<int, List<Transition>>> triggerTransitionsFromAny = new();

        private int _layerCount;

        #region - Initialization -
        public LayeredStateMachine(string name, bool canExitInstantly = false) : base(name, canExitInstantly)
        {

        }

        /// <summary>
        /// Throws an exception if the state machine is not initialised yet.
        /// </summary>
        /// <param name="context">String message for which action the fsm should
        /// 	be initialised for.</param>
        private void EnsureIsInitializedFor()
        {
            if (activeStates == null)
            {
                Debug.LogError(StateMachineExceptions.StateMachineNotInitialized);
                return;
            }
            if (activeStates.Count == 0)
            {
                Debug.LogError(StateMachineExceptions.NoDefaultStateFound);
            }
        }

        /// <summary>
        /// Defines the entry point of the state machine
        /// </summary>
        /// <param name="name">The name / identifier of the start state</param>
        public void SetDefaultState(int name, int layer)
        {
            startStates[layer] = (layer, name, true);
        }

        /// <summary>
        /// Defines the entry point of the state machine
        /// </summary>
        /// <param name="name">The name / identifier of the start state</param>
        public void SetDefaultState(State name, int layer)
        {
            startStates[layer] = (layer, name.ID, true);
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
            foreach (var startingState in startStates.Values)
            {
                if (!startingState.hasState)
                {
                    Debug.LogError(StateMachineExceptions.NoDefaultStateFound);
                }
            }
            if(startStates.Count == 0)
            {
                Debug.LogError(StateMachineExceptions.NoDefaultStateFound);
            }

            foreach (var startingState in startStates.Values)
            {
                ChangeState(startingState.layer, startingState.state);
            }

            foreach (var layer in transitionsFromAny.Values)
            {
                foreach (var t in layer)
                {
                    t.OnEnter();
                }
            }

            foreach (var layer in triggerTransitionsFromAny.Values)
            {
                foreach (var transitions in layer.Values)
                {
                    foreach (var t in transitions)
                    {
                        t.OnEnter();
                    }
                }
            }
        }

        /// <summary>
		/// Runs one logic step. It does at most one transition itself and
		/// calls the active state's logic function (after the state transition, if
		/// one occurred).
		/// </summary>
		public override void OnUpdate()
        {
            EnsureIsInitializedFor();

            for (int i = 0; i < _layerCount; i++)
            {
                if (!TryAllGlobalTransitions(i))
                {
                    TryAllDirectTransitions(i);
                }

                activeStates[i].OnUpdate();
            }
        }

        public override void OnExit()
        {
            if (activeStates != null && activeStates.Count > 0)
            {
                foreach (var state in activeStates)
                {
                    state.Value.OnExit();
                    activeStates[state.Key] = null;
                }
                // By setting the activeState to null, the state's onExit method won't be called
                // a second time when the state machine enters again (and changes to the start state)
            }
        }

        /// <summary>
        /// Notifies the state machine that the state can cleanly exit,
        /// and if a state change is pending, it will execute it.
        /// </summary>
        public void StateCanExit()
        {
            for (int i = 0; i < _layerCount; i++)
            {
                var pendingState = pendingStates[i];
                if (pendingState.isPending)
                {
                    int layer = pendingState.layer;
                    int state = pendingState.state;
                    // When the pending state is a ghost state, ChangeState() will have
                    // to try all outgoing transitions, which may overwrite the pendingState.
                    // That's why it is first cleared, and not afterwards, as that would overwrite
                    // a new, valid pending state.
                    pendingStates[layer] = (layer, EMPTY_STATE, false);
                    ChangeState(layer, state);
                }
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
		private void ChangeState(int layer, int name)
        {
            activeStates[layer]?.OnExit();

            if (!nameToStateBundle.TryGetValue(new (layer, name), out StateBundle bundle) || bundle.state == null)
            {
                if (stateToStringMap.TryGetValue(new(layer, name), out string nameString))
                {
                    Debug.LogError(StateMachineExceptions.StateNotFound(nameString));
                }
                else
                {
                    Debug.LogError(StateMachineExceptions.StateNotFound(name.ToString()));
                }
            }

            activeTransitions[layer] = bundle.transitions ?? noTransitions;
            activeTriggerTransitions[layer] = bundle.triggerToTransitions ?? noTriggerTransitions;

            activeStates[layer] = bundle.state;
            activeStates[layer].OnEnter();

            foreach (var t in activeTransitions[layer])
            {
                t.OnEnter();
            }

            foreach (var transitions in activeTriggerTransitions[layer].Values)
            {
                foreach (Transition t in transitions)
                {
                    t.OnEnter();
                }
            }

            if (activeStates[layer].CanExitInstantly)
            {
                TryAllDirectTransitions(layer);
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
                ChangeState(layer, name);
            }
            else
            {
                pendingStates[layer] = (layer, name, true);
                activeStates[layer].OnExitRequest();
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
		public void RequestStateChange(int name, bool force = false)
        {
            if (force)
            {
                ChangeState(0, name);
            }
            else
            {
                pendingStates[0] = (0, name, true);
                activeStates[0].OnExitRequest();
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
		private StateBundle GetOrCreateStateBundle(int layer, int name)
        {
            if (!nameToStateBundle.TryGetValue(new(layer, name), out StateBundle bundle))
            {
                bundle = new StateBundle();
                nameToStateBundle.Add(new(layer, name), bundle);
            }

            return bundle;
        }

        /// <summary>
		/// Adds a new node / state to the state machine.
		/// </summary>
		/// <param name="name">The name / identifier of the new state</param>
		/// <param name="state">The new state instance, e.g. <c>State</c>, <c>CoState</c>, <c>StateMachine</c></param>
		public void AddState(State state, int layer)
        {
            state.StateMachine = this;
            state.Init();

            StateBundle bundle = GetOrCreateStateBundle(layer, state.ID);
            bundle.state = state;
            stateToStringMap.Add(new(layer, state.ID), state.Name);

            if (!startStates.ContainsKey(layer) || !startStates[layer].hasState)
            {
                SetDefaultState(state.ID, layer);
                pendingStates[layer] = (layer, state.ID, false);
                activeStates.Add(layer, null);
                transitionsFromAny.Add(layer, new());
                _layerCount++;
            }
        }

        public State GetState(int layer, string name)
        {
            if (!nameToStateBundle.TryGetValue(new (layer, name.GetHashCode()), out StateBundle bundle) || bundle.state == null)
            {
                Debug.LogError(StateMachineExceptions.StateNotFound(name));
            }
            return bundle.state;
        }

        public State GetState(int layer, int name)
        {
            if (!nameToStateBundle.TryGetValue(new(layer, name.GetHashCode()), out StateBundle bundle) || bundle.state == null)
            {
                if (stateToStringMap.TryGetValue(new(layer, name), out string nameString))
                {
                    Debug.LogError(StateMachineExceptions.StateNotFound(nameString));
                }
                else
                {
                    Debug.LogError(StateMachineExceptions.StateNotFound(name.ToString()));
                }
            }
            return bundle.state;
        }

        public T GetSubStateMachine<T>(int layer, string name) where T : State, IStateMachine
        {
            State state = GetState(layer, name);
            return state as T;
        }

        public T GetSubStateMachine<T>(int layer, int name) where T : State, IStateMachine
        {
            State state = GetState(layer, name);
            return state as T;
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
        public void AddTransition(Transition transition, int layer)
        {
            InitTransition(transition);

            StateBundle bundle = GetOrCreateStateBundle(layer, transition.From);
            bundle.AddTransition(transition);
        }

        /// <summary>
		/// Adds a new transition that can happen from any possible state
		/// </summary>
		/// <param name="transition">The transition instance; The "from" field can be
		/// left empty, as it has no meaning in this context.</param>
		public void AddTransitionFromAny(Transition transition, int layer)
        {
            InitTransition(transition);

            transitionsFromAny[layer].Add(transition);
        }

        /// <summary>
		/// Adds a new trigger transition between two states that is only checked
		/// when the specified trigger is activated.
		/// </summary>
		/// <param name="trigger">The name / identifier of the trigger</param>
		/// <param name="transition">The transition instance, e.g. Transition, TransitionAfter, ...</param>
		public void AddTriggerTransition(int trigger, Transition transition, int layer)
        {
            InitTransition(transition);

            StateBundle bundle = GetOrCreateStateBundle(layer, transition.From);
            bundle.AddTriggerTransition(trigger, transition);
        }

        /// <summary>
		/// Adds a new trigger transition that can happen from any possible state, but is only
		/// checked when the specified trigger is activated.
		/// </summary>
		/// <param name="trigger">The name / identifier of the trigger</param>
		/// <param name="transition">The transition instance; The "from" field can be
		/// left empty, as it has no meaning in this context.</param>
		public void AddTriggerTransitionFromAny(int trigger, Transition transition, int layer)
        {
            InitTransition(transition);

            if (!triggerTransitionsFromAny[layer].TryGetValue(trigger, out List<Transition> transitionsOfTrigger))
            {
                transitionsOfTrigger = new List<Transition>();
                triggerTransitionsFromAny[layer].Add(trigger, transitionsOfTrigger);
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
		public void AddTwoWayTransition(Transition transition, int layer)
        {
            InitTransition(transition);
            AddTransition(transition, layer);

            Transition reverse = transition.Reverse();
            InitTransition(reverse);
            AddTransition(reverse, layer);
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
		public void AddTwoWayTriggerTransition(int trigger, Transition transition, int layer)
        {
            InitTransition(transition);
            AddTriggerTransition(trigger, transition, layer);

            Transition reverse = transition.Reverse();
            InitTransition(reverse);
            AddTriggerTransition(trigger, reverse, layer);
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
		private bool TryAllDirectTransitions(int layer)
        {
            if (DetermineTransition(activeTransitions[layer], layer, out var to))
            {
                RequestStateChange(layer, to);
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
		private bool TryAllGlobalTransitions(int layer)
        {
            if (DetermineTransition(transitionsFromAny[layer], layer, out var to))
            {
                RequestStateChange(layer, to);
                return true;
            }
            else
            {
                return false;
            }
        }

        private bool DetermineTransition(List<Transition> transitions, int layer, out int ToState)
        {
            int desire = 0;
            ToState = EMPTY_STATE;
            foreach (Transition transition in transitions)
            {
                // Don't transition to the "to" state, if that state is already the active state
                if (transition.To == activeStates[layer].ID)
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
		private bool TryTrigger(int trigger, int layer)
        {
            EnsureIsInitializedFor();

            if (triggerTransitionsFromAny[layer].TryGetValue(trigger, out List<Transition> triggerTransitions))
            {
                if (DetermineTransition(triggerTransitions, layer, out var to))
                {
                    RequestStateChange(layer, to);
                    return true;
                }
            }

            if (activeTriggerTransitions[layer].TryGetValue(trigger, out triggerTransitions))
            {
                if (DetermineTransition(triggerTransitions, layer, out var to))
                {
                    RequestStateChange(layer, to);
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
		public void Trigger(int trigger, int layer)
        {
            // If a transition occurs, then the trigger should not be activated
            // in the new active state, that the state machine just switched to.
            if (TryTrigger(trigger, layer))
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
		public void OnInvokeAction(int actionID, int layer)
        {
            EnsureIsInitializedFor();
            activeStates[layer]?.OnAction(actionID);
        }

        /// <summary>
        /// Runs an action on the currently active state and lets you pass one data parameter.
        /// </summary>
        /// <param name="trigger">Name of the action</param>
        /// <param name="data">Any custom data for the parameter</param>
        /// <typeparam name="TData">Type of the data parameter.
        /// 	Should match the data type of the action that was added via AddAction<T>(...).</typeparam>
        public void OnInvokeAction<TData>(int actionID, int layer, TData data)
        {
            EnsureIsInitializedFor();
            activeStates[layer]?.OnAction(actionID, data);
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
