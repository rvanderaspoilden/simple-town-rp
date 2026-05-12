using System;
using System.Collections.Generic;
using Mirror;

namespace Sim.Jobs {
    /// <summary>
    /// Miroir client des missions actives du joueur local. Souscrit aux
    /// messages serveur et expose des events C# que le HUD (à venir) peut
    /// écouter — même pattern que ApiManager / ClientNpcManager.
    /// </summary>
    public class JobClientManager {
        private static JobClientManager _instance;
        public static JobClientManager Instance => _instance ??= new JobClientManager();

        private readonly Dictionary<string, JobClientState> _states = new Dictionary<string, JobClientState>();
        private bool _handlersRegistered;

        public IReadOnlyDictionary<string, JobClientState> States => _states;

        public event Action<JobClientState> JobOffered;
        public event Action<JobClientState> JobStepAdvanced;
        public event Action<JobClientState> JobFinished;

        public void RegisterHandlers() {
            if (_handlersRegistered) return;
            NetworkClient.RegisterHandler<JobOfferedMessage>(OnOffered);
            NetworkClient.RegisterHandler<JobStepAdvancedMessage>(OnStepAdvanced);
            NetworkClient.RegisterHandler<JobFinishedMessage>(OnFinished);
            _handlersRegistered = true;
        }

        public void UnregisterHandlers() {
            if (!_handlersRegistered) return;
            NetworkClient.UnregisterHandler<JobOfferedMessage>();
            NetworkClient.UnregisterHandler<JobStepAdvancedMessage>();
            NetworkClient.UnregisterHandler<JobFinishedMessage>();
            _handlersRegistered = false;
        }

        public void ClearAll() {
            _states.Clear();
        }

        public void SendAccept(string instanceId) {
            if (!NetworkClient.isConnected) return;
            NetworkClient.Send(new JobAcceptedMessage { instanceId = instanceId });
        }

        public void SendAbandon(string instanceId) {
            if (!NetworkClient.isConnected) return;
            NetworkClient.Send(new JobAbandonRequestMessage { instanceId = instanceId });
        }

        private void OnOffered(JobOfferedMessage msg) {
            var def = JobDatabase.GetById(msg.jobId);
            var state = new JobClientState {
                InstanceId = msg.instanceId,
                Definition = def,
                CurrentStepIndex = 0,
                PrimaryTargetKind = msg.primaryTargetKind,
                PrimaryTargetId = msg.primaryTargetId,
                SecondaryTargetKind = msg.secondaryTargetKind,
                SecondaryTargetId = msg.secondaryTargetId,
                PayloadItemId = msg.payloadItemId,
                Status = JobStatus.Offered
            };
            _states[msg.instanceId] = state;
            JobOffered?.Invoke(state);
        }

        private void OnStepAdvanced(JobStepAdvancedMessage msg) {
            if (!_states.TryGetValue(msg.instanceId, out var state)) return;
            state.CurrentStepIndex = msg.newStepIndex;
            state.CurrentPromptKey = msg.promptKey;
            state.Status = JobStatus.Active;
            JobStepAdvanced?.Invoke(state);
        }

        private void OnFinished(JobFinishedMessage msg) {
            if (!_states.TryGetValue(msg.instanceId, out var state)) return;
            state.Status = msg.terminalStatus;
            state.FailureReason = msg.failureReason;
            JobFinished?.Invoke(state);
            _states.Remove(msg.instanceId);
        }
    }

    /// <summary>État local d'une mission tel que vu par le client.</summary>
    public class JobClientState {
        public string InstanceId;
        public JobDefinition Definition;
        public int CurrentStepIndex;
        public string CurrentPromptKey;
        public JobStatus Status;
        public JobFailureReason FailureReason;

        public JobTargetKind PrimaryTargetKind;
        public string PrimaryTargetId;
        public JobTargetKind SecondaryTargetKind;
        public string SecondaryTargetId;
        public string PayloadItemId;
    }
}
