using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Sim.Missions {
    /// <summary>
    /// Miroir client des missions actives du joueur local. Souscrit aux
    /// messages serveur et expose des events C# que le HUD (à venir) peut
    /// écouter — même pattern que ApiManager / ClientNpcManager.
    /// </summary>
    public class MissionClientManager {
        private static MissionClientManager _instance;
        public static MissionClientManager Instance => _instance ??= new MissionClientManager();

        private readonly Dictionary<string, MissionClientState> _states = new Dictionary<string, MissionClientState>();
        private bool _handlersRegistered;

        public IReadOnlyDictionary<string, MissionClientState> States => _states;

        public event Action<MissionClientState> MissionOffered;
        public event Action<MissionClientState> MissionStepAdvanced;
        public event Action<MissionClientState> MissionFinished;
        public event Action<MissionClientState, MissionSortProgressMessage> SortProgress;

        public void RegisterHandlers() {
            if (_handlersRegistered) return;
            NetworkClient.RegisterHandler<MissionOfferedMessage>(OnOffered);
            NetworkClient.RegisterHandler<MissionStepAdvancedMessage>(OnStepAdvanced);
            NetworkClient.RegisterHandler<MissionFinishedMessage>(OnFinished);
            NetworkClient.RegisterHandler<MissionRewardNotificationMessage>(OnRewardNotification);
            NetworkClient.RegisterHandler<MissionNotificationMessage>(OnJobNotification);
            NetworkClient.RegisterHandler<MissionSortProgressMessage>(OnSortProgress);
            NetworkClient.RegisterHandler<MissionSortItemsSpawnedMessage>(OnSortItemsSpawned);
            _handlersRegistered = true;
        }

        public void UnregisterHandlers() {
            if (!_handlersRegistered) return;
            NetworkClient.UnregisterHandler<MissionOfferedMessage>();
            NetworkClient.UnregisterHandler<MissionStepAdvancedMessage>();
            NetworkClient.UnregisterHandler<MissionFinishedMessage>();
            NetworkClient.UnregisterHandler<MissionRewardNotificationMessage>();
            NetworkClient.UnregisterHandler<MissionNotificationMessage>();
            NetworkClient.UnregisterHandler<MissionSortProgressMessage>();
            NetworkClient.UnregisterHandler<MissionSortItemsSpawnedMessage>();
            _handlersRegistered = false;
        }

        private void OnJobNotification(MissionNotificationMessage msg) {
            if (NotificationManager.Instance == null) return;
            if (string.IsNullOrEmpty(msg.text)) return;
            NotificationManager.Instance.AddNotification(msg.text, PhoneAppIds.Career);
        }

        private void OnRewardNotification(MissionRewardNotificationMessage msg) {
            if (NotificationManager.Instance == null) return;
            var text = string.IsNullOrEmpty(msg.label)
                ? $"+{msg.amount} €"
                : $"{msg.label} : +{msg.amount} €";
            NotificationManager.Instance.AddNotification(text, PhoneAppIds.Bank);
        }

        public void ClearAll() {
            _states.Clear();
        }

        public void SendAccept(string instanceId) {
            if (!NetworkClient.isConnected) return;
            NetworkClient.Send(new MissionAcceptedMessage { instanceId = instanceId });
        }

        public void SendAbandon(string instanceId) {
            if (!NetworkClient.isConnected) return;
            NetworkClient.Send(new MissionAbandonRequestMessage { instanceId = instanceId });
        }

        private void OnOffered(MissionOfferedMessage msg) {
            var def = MissionDatabase.GetById(msg.missionId);
            var state = new MissionClientState {
                InstanceId = msg.instanceId,
                Definition = def,
                CurrentStepIndex = msg.currentStepIndex,
                CurrentPromptKey = msg.currentPromptKey,
                CurrentTargetId = msg.currentTargetId,
                CurrentTargetName = msg.currentTargetName,
                ShowTargetBeacon = msg.showTargetBeacon,
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
            MissionOffered?.Invoke(state);
        }

        private void OnStepAdvanced(MissionStepAdvancedMessage msg) {
            if (!_states.TryGetValue(msg.instanceId, out var state)) return;
            state.CurrentStepIndex = msg.newStepIndex;
            state.CurrentPromptKey = msg.promptKey;
            state.CurrentTargetId = msg.currentTargetId;
            state.CurrentTargetName = msg.currentTargetName;
            state.ShowTargetBeacon = msg.showTargetBeacon;
            state.Status = MissionStatus.Active;
            MissionStepAdvanced?.Invoke(state);
        }

        private void OnSortProgress(MissionSortProgressMessage msg) {
            _states.TryGetValue(msg.instanceId, out var state);
            SortProgress?.Invoke(state, msg);
        }

        // Applique la catégorie de tri (donnée métier) aux colis déjà spawnés, pour
        // teinter leur étiquette de la couleur du bac correspondant (PackageJobItemBehaviour).
        private void OnSortItemsSpawned(MissionSortItemsSpawnedMessage msg) {
            if (msg.entityIds == null || msg.categories == null) return;
            int count = Mathf.Min(msg.entityIds.Length, msg.categories.Length);
            for (int i = 0; i < count; i++) {
                var item = ClientItemManager.Instance.GetItem(msg.entityIds[i]);
                if (item == null) continue;
                var package = item.GetComponent<PackageJobItemBehaviour>();
                if (package != null) package.SetSortingCategory(msg.categories[i]);
            }
        }

        private void OnFinished(MissionFinishedMessage msg) {
            if (!_states.TryGetValue(msg.instanceId, out var state)) return;
            state.Status = msg.terminalStatus;
            state.FailureReason = msg.failureReason;
            state.CompletionRating = msg.rating;
            state.CompletionElapsedSeconds = msg.elapsedSeconds;
            state.CompletionCorrectCount = msg.correctCount;
            state.CompletionTotalCount = msg.totalCount;
            state.CompletionMoneyEarned = msg.moneyEarned;
            state.CompletionConstellationLabels  = msg.constellationGainLabels;
            state.CompletionConstellationAmounts = msg.constellationGainAmounts;
            MissionFinished?.Invoke(state);
            _states.Remove(msg.instanceId);
        }
    }

    /// <summary>État local d'une mission tel que vu par le client.</summary>
    public class MissionClientState {
        public string InstanceId;
        public MissionDefinition Definition;
        public int CurrentStepIndex;
        public string CurrentPromptKey;
        public string CurrentTargetId;
        public string CurrentTargetName;
        public bool ShowTargetBeacon;
        public MissionStatus Status;
        public MissionFailureReason FailureReason;

        public MissionTargetKind PrimaryTargetKind;
        public string PrimaryTargetId;
        public string PrimaryTargetName;
        public MissionTargetKind SecondaryTargetKind;
        public string SecondaryTargetId;
        public string SecondaryTargetName;
        public string PayloadItemId;

        // Server's elapsed time when MissionOfferedMessage was sent, plus the local
        // Time.unscaledTime at which we received it. The HUD computes the
        // remaining mission time by extrapolating from these two values.
        public float ElapsedSecondsAtSync;
        public float SyncedAtUnscaled;

        // Populated on MissionFinished (Completed status only).
        public byte CompletionRating;
        public float CompletionElapsedSeconds;
        public int CompletionCorrectCount;
        public int CompletionTotalCount;
        public int CompletionMoneyEarned;
        // Parallel arrays : CompletionConstellationLabels[i] correspond à
        // CompletionConstellationAmounts[i]. Libellés déjà résolus par le serveur
        // (ex "Ingénieux", "Livreur") pour l'affichage direct dans le toast.
        public string[] CompletionConstellationLabels;
        public int[]    CompletionConstellationAmounts;
    }
}
