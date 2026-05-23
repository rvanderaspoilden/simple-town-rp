using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

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
        public event Action<JobClientState, JobSortProgressMessage> SortProgress;

        public void RegisterHandlers() {
            if (_handlersRegistered) return;
            NetworkClient.RegisterHandler<JobOfferedMessage>(OnOffered);
            NetworkClient.RegisterHandler<JobStepAdvancedMessage>(OnStepAdvanced);
            NetworkClient.RegisterHandler<JobFinishedMessage>(OnFinished);
            NetworkClient.RegisterHandler<JobRewardNotificationMessage>(OnRewardNotification);
            NetworkClient.RegisterHandler<JobNotificationMessage>(OnJobNotification);
            NetworkClient.RegisterHandler<JobSortProgressMessage>(OnSortProgress);
            NetworkClient.RegisterHandler<JobSortItemsSpawnedMessage>(OnSortItemsSpawned);
            _handlersRegistered = true;
        }

        public void UnregisterHandlers() {
            if (!_handlersRegistered) return;
            NetworkClient.UnregisterHandler<JobOfferedMessage>();
            NetworkClient.UnregisterHandler<JobStepAdvancedMessage>();
            NetworkClient.UnregisterHandler<JobFinishedMessage>();
            NetworkClient.UnregisterHandler<JobRewardNotificationMessage>();
            NetworkClient.UnregisterHandler<JobNotificationMessage>();
            NetworkClient.UnregisterHandler<JobSortProgressMessage>();
            NetworkClient.UnregisterHandler<JobSortItemsSpawnedMessage>();
            _handlersRegistered = false;
        }

        private void OnJobNotification(JobNotificationMessage msg) {
            if (NotificationManager.Instance == null) return;
            if (string.IsNullOrEmpty(msg.text)) return;
            NotificationManager.Instance.AddNotification(msg.text, NotificationType.JOB);
        }

        private void OnRewardNotification(JobRewardNotificationMessage msg) {
            if (NotificationManager.Instance == null) return;
            var text = string.IsNullOrEmpty(msg.label)
                ? $"+{msg.amount} €"
                : $"{msg.label} : +{msg.amount} €";
            NotificationManager.Instance.AddNotification(text, NotificationType.BANK);
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
                CurrentStepIndex = msg.currentStepIndex,
                CurrentPromptKey = msg.currentPromptKey,
                CurrentTargetId = msg.currentTargetId,
                CurrentTargetName = msg.currentTargetName,
                PrimaryTargetKind = msg.primaryTargetKind,
                PrimaryTargetId = msg.primaryTargetId,
                PrimaryTargetName = msg.primaryTargetName,
                SecondaryTargetKind = msg.secondaryTargetKind,
                SecondaryTargetId = msg.secondaryTargetId,
                SecondaryTargetName = msg.secondaryTargetName,
                PayloadItemId = msg.payloadItemId,
                Status = msg.Status,
                ElapsedSecondsAtSync = msg.elapsedSeconds,
                SyncedAtUnscaled = Time.unscaledTime,
            };
            _states[msg.instanceId] = state;
            JobOffered?.Invoke(state);
        }

        private void OnStepAdvanced(JobStepAdvancedMessage msg) {
            if (!_states.TryGetValue(msg.instanceId, out var state)) return;
            state.CurrentStepIndex = msg.newStepIndex;
            state.CurrentPromptKey = msg.promptKey;
            state.CurrentTargetId = msg.currentTargetId;
            state.CurrentTargetName = msg.currentTargetName;
            state.Status = JobStatus.Active;
            JobStepAdvanced?.Invoke(state);
        }

        private void OnSortProgress(JobSortProgressMessage msg) {
            _states.TryGetValue(msg.instanceId, out var state);
            SortProgress?.Invoke(state, msg);
        }

        // Applique la catégorie de tri (donnée métier) aux colis déjà spawnés, pour
        // teinter leur étiquette de la couleur du bac correspondant (PackageJobItemBehaviour).
        private void OnSortItemsSpawned(JobSortItemsSpawnedMessage msg) {
            if (msg.entityIds == null || msg.categories == null) return;
            int count = Mathf.Min(msg.entityIds.Length, msg.categories.Length);
            for (int i = 0; i < count; i++) {
                var item = ClientItemManager.Instance.GetItem(msg.entityIds[i]);
                if (item == null) continue;
                var package = item.GetComponent<PackageJobItemBehaviour>();
                if (package != null) package.SetSortingCategory(msg.categories[i]);
            }
        }

        private void OnFinished(JobFinishedMessage msg) {
            if (!_states.TryGetValue(msg.instanceId, out var state)) return;
            state.Status = msg.terminalStatus;
            state.FailureReason = msg.failureReason;
            state.CompletionRating = msg.rating;
            state.CompletionElapsedSeconds = msg.elapsedSeconds;
            state.CompletionCorrectCount = msg.correctCount;
            state.CompletionTotalCount = msg.totalCount;
            state.CompletionVariant = (JobResultVariant)msg.resultVariant;
            state.CompletionMoneyEarned = msg.moneyEarned;
            state.CompletionXpEarned = msg.xpEarned;
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
        public string CurrentTargetId;
        public string CurrentTargetName;
        public JobStatus Status;
        public JobFailureReason FailureReason;

        public JobTargetKind PrimaryTargetKind;
        public string PrimaryTargetId;
        public string PrimaryTargetName;
        public JobTargetKind SecondaryTargetKind;
        public string SecondaryTargetId;
        public string SecondaryTargetName;
        public string PayloadItemId;

        // Server's elapsed time when JobOfferedMessage was sent, plus the local
        // Time.unscaledTime at which we received it. The HUD computes the
        // remaining mission time by extrapolating from these two values.
        public float ElapsedSecondsAtSync;
        public float SyncedAtUnscaled;

        // Populated on JobFinished (Completed status only).
        public byte CompletionRating;
        public float CompletionElapsedSeconds;
        public int CompletionCorrectCount;
        public int CompletionTotalCount;
        public JobResultVariant CompletionVariant;
        public int CompletionMoneyEarned;
        public int CompletionXpEarned;
    }
}
