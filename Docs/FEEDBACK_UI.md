# Feedback joueur : Toast vs Notification

Deux canaux de retour visuel. **La règle de choix dépend de la temporalité du résultat
par rapport à l'action du joueur.**

## Règle

| Canal | Quand l'utiliser | Exemples |
|---|---|---|
| **Toast** (`WorldToastManager`) — bulle flottante au-dessus du joueur | Résultat **immédiat / synchrone** d'une action du joueur (feedback instantané) | Achat au distributeur, ramassage refusé (mains pleines / trop loin), jet à la poubelle, fin de mission (XP/argent), crédit social gagné, fonds insuffisants à l'achat |
| **Notification** (`NotificationManager`) — bandeau coin d'écran | Résultat **asynchrone / différé** qui ne survient PAS instantanément suite à l'action, ou événement non déclenché par le joueur | Achat boutique téléphone → **livraison à domicile** (arrive plus tard), salaire périodique, vol de BC à l'évanouissement, progression / événements de mission |

Résumé : **action joueur → résultat tout de suite = Toast** ; **résultat décalé dans le
temps ou événement passif = Notification**.

## Localité réseau des toasts

- **Par défaut un toast est LOCAL** : `WorldToastManager.Show(...)` l'affiche au-dessus du
  **joueur local uniquement**, sans aucun trafic réseau. C'est le cas de la grande majorité.
- Le déclencheur peut venir du serveur (`ToastNotificationMessage` avec `worldToast=true`,
  envoyé à la connexion de l'acteur) mais **l'affichage reste local** à ce client.
- **Option opt-in de synchronisation** : pour qu'un toast soit vu par les AUTRES joueurs
  au-dessus d'un joueur précis, le serveur broadcast un `S2C_WorldToast { anchorNetId,
  title, subtitle, delay }` aux connexions voulues (ex. tous les clients d'une room). Chaque
  client le relaie via `WorldToastManager.ShowAbove(anchorNetId, ...)`. À n'utiliser que
  pour les rares cas réellement partagés.

## API

```csharp
// LOCAL (défaut) — au-dessus du joueur local
WorldToastManager.Show("Mission terminée", "+1200 €   +30 XP ⭐", delay: 0.25f);
WorldToastManager.Show("Mains pleines");                       // une ligne

// RÉSEAU (option) — déclenché par S2C_WorldToast broadcasté par le serveur
WorldToastManager.ShowAbove(playerNetId, "Titre", "Sous-titre");

// NOTIFICATION coin d'écran (résultat asynchrone / passif)
NotificationManager.Instance.AddNotification("Salaire versé : +50 €", NotificationType.BANK);

// Canal serveur générique : ToastNotificationMessage { text, typeByte, worldToast }
//   worldToast = true  → toast flottant (feedback d'action banal)
//   worldToast = false → notification coin d'écran (défaut : salaire, etc.)
```
