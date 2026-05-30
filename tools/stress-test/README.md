# Stress Test — Bots

Lance N processus Unity headless qui s'authentifient comme bots, se connectent au serveur Mirror, et se déplacent aléatoirement en ville. Sert à mesurer la résistance du serveur sous charge.

## Prérequis

### Backend
1. Backend `simple-town-ws` lancé (`npm run start:dev` dans `apps/api`).
2. `.env` configuré avec :
   - `BOT_REGISTRATION_SECRET=<un hex aléatoire>`
   - `BOT_DEFAULT_PRESET=<nom d'un preset apartment>` (défaut: `default`)
   - `NODE_ENV` **non** égal à `production`
3. Le preset apartment référencé doit exister dans la table `presets`.

### Unity
1. Le serveur Mirror tourne (Editor en mode normal sur la scène `City`, ou un build serveur dédié).
2. Build **Bot Headless** sortant dans `Builds/Bot/Simple Town.exe` (Windows Standalone x64, pas de Server Build, sans graphics).

## Usage

```powershell
$env:BOT_REGISTRATION_SECRET = "ton_secret_ici"

# Lance 10 bots
./launch-bots.ps1 -Count 10

# Plus de bots, sur serveur distant
./launch-bots.ps1 -Count 50 -Server http://192.168.1.42:3000 -StaggerMs 500

# Ajoute 20 bots supplémentaires sans toucher aux 10 déjà lancés
./launch-bots.ps1 -Count 20 -StartIndex 10

# Coupe tous les bots
./stop-bots.ps1

# Purge la DB des comptes bot_*
./cleanup-bots.ps1
```

## Logs

Chaque bot écrit son log Unity dans `Builds/Bot/logs/bot_NNNNN.log`. `[Bot]` est le tag utilisé par `BotRunner`. Surveiller en particulier :
- `register-bot failed` → secret mauvais ou backend down
- `Disconnected — reconnecting` → kick serveur (probablement la limite "already online")
- `failed to load character` → DB ou JWT cassé

## Comportement V1

- Auth via `POST /auth/register-bot` (un round-trip)
- Connexion Mirror via le path normal `OnClientConnect → CreateCharacterMessage`
- Après spawn du `PlayerController.Local` : pick un point NavMesh aléatoire dans un rayon de 15m, MoveTo, attendre 3-8s, recommencer
- En cas de disconnect : retry après 5s

Hors scope V1 : chat, interactions props, jobs, achats. Voir le plan pour V2.
