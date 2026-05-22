using Mirror;
using Sim.Entities.Persistence;
using Sim.Logging;
using UnityEngine;

namespace Sim.Jobs {
    /// <summary>
    /// Server-only. Every `cities.salary_period_seconds` seconds, pays each
    /// online player either:
    ///   - their job's `salaryAmount` if they have a `CurrentJobCategory`, or
    ///   - the city's `unemployed_income` otherwise (allocation chômage).
    /// POC behaviour: online-only — disconnected players accrue nothing.
    ///
    /// Owned by JobSystemBootstrap (created on OnServerStart, destroyed on
    /// OnServerStop).
    /// </summary>
    public class PlayerCareerSalaryTicker : MonoBehaviour {
        private float _accumulator;
        private float _cachedPeriodSeconds;

        private void Update() {
            if (!NetworkServer.active) return;

            float period = ResolvePeriodSeconds();
            if (period <= 0f) return;

            _accumulator += Time.deltaTime;
            if (_accumulator < period) return;
            _accumulator -= period;

            PayAllPlayers();
        }

        private float ResolvePeriodSeconds() {
            // Cache once the city data is hydrated; falls back to the previous
            // value otherwise so the ticker doesn't drift when the CityData
            // value momentarily reads 0 (e.g. between RetrieveCityData calls).
            var net = NetworkManager.singleton as SimpleTownNetwork;
            if (net != null) {
                int periodFromCity = net.CityData.SalaryPeriodSeconds;
                if (periodFromCity > 0) _cachedPeriodSeconds = periodFromCity;
            }
            return _cachedPeriodSeconds;
        }

        private static void PayAllPlayers() {
            var net = NetworkManager.singleton as SimpleTownNetwork;
            int unemploymentIncome = net != null ? net.CityData.UnemployedIncome : 0;

            foreach (var conn in NetworkServer.connections.Values) {
                if (conn?.identity == null) continue;
                var player = conn.identity.GetComponent<PlayerController>();
                if (player == null || player.CharacterData == null) continue;

                var bank = player.GetComponent<PlayerBankAccount>();
                if (bank == null) continue;

                var category = player.CharacterData.CurrentJobCategory;
                int amount;
                string label;
                if (category.HasValue) {
                    amount = JobDatabase.GetSalaryForCategory(category.Value);
                    label = $"Salaire {JobCategoryLabels.Display(category)}";
                } else {
                    amount = unemploymentIncome;
                    label = "Allocation chômage";
                }
                if (amount <= 0) continue;

                bank.PostLedger(amount, LedgerReason.Salary, LedgerCounterparty.System, LedgerCounterparty.Job);

                conn.Send(new ToastNotificationMessage {
                    text = $"{label} : +{amount} €",
                    typeByte = (byte)NotificationType.BANK,
                });

                GameLogger.System.Debug("CareerPayout {NetId} {Category} {Amount}",
                    conn.identity.netId, category, amount);
            }
        }
    }
}
