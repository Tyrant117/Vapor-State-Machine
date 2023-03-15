using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace VaporStateMachine.Playables
{
    public class PlayableState
    {
        private AnimationClipPlayable _playable;

        public AnimationClipPlayable Playable
        {
            get { return _playable; }
        }

        private AnimationMixerPlayable _mixer;
        private int _inputPort;

        public virtual void Init(PlayableGraph graph, AnimationMixerPlayable mixer, AnimationClip clip)
        {
            _mixer = mixer;
            _playable = AnimationClipPlayable.Create(graph, clip);
            _inputPort = _mixer.AddInput(_playable, 0);
        }

        public virtual void OnEnter()
        {
            _mixer.SetInputWeight(_inputPort, 1f);
        }

        public virtual void OnUpdate()
        {

        }

        public virtual void OnExit()
        {
            _mixer.SetInputWeight(_inputPort, 0f);
        }
    }
}
