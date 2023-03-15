using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VaporStateMachine
{
    public class StateMachineExceptions
    {
        public static string StateMachineNotInitialized => $"The state machine has not been initialized.\n" +
                $"Call SetDefaultState, Init(), or OnEnter to initialize.";

        public static string StateNotFound(string state)
        {
            return $"The state <b>[{state}]</b> does not exist.\n" +
                "Check for typos in the state names.\n" +
                "Ensure the state is in the state machine.";
        }

        public static string ActionTypeMismatch(Type type, Delegate action)
        {
            return $"The expected argument type ({type}) does not match the type of the added action ({action}).";
        }

        public static string NoDefaultStateFound => $"The state machine does not have any states before OnEnter was called.";
    }
}
