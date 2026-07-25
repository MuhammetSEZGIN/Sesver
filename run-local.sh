#!/usr/bin/env bash
# Tüm Sesver mikroservislerini yerelde (Docker'sız) ayağa kaldırır.
#
# Ön koşul: altyapı servisleri (Postgres x3, MongoDB, RabbitMQ, Redis x2) çalışıyor
# olmalı — yoksa önce şunu çalıştırın:
#   docker compose -f docker-compose.local.yml up -d
#
# Kullanım:
#   ./run-local.sh              # tüm .NET servisleri + VersionControlService + AuthenticationService + VoiceService
#   ./run-local.sh --no-voice   # VoiceService hariç (LiveKit kurulu değilse)
#   ./run-local.sh --no-auth    # AuthenticationService (Java) hariç (Maven/JDK kurulu değilse)
#
# Not: AuthenticationService olmadan [Authorize(Roles = "...")] ile korunan
# endpoint'ler (ClanController/ClanMembershipController/ChannelController vb.)
# rol claim'i (X-Clan-Role) hiç dolmayacağı için başarısız olur.
#
# Durdurmak için Ctrl+C — tüm alt süreçler birlikte kapatılır.

set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

# Job control açık: her arka plan işi kendi process group'unu alır, böylece
# cleanup() sadece dış subshell'i değil (dotnet run / mvnw'nin fork ettiği
# gerçek uygulama process'i dahil) tüm grubu öldürebilir.
set -m

RUN_VOICE=true
RUN_AUTH=true
for arg in "$@"; do
  case "$arg" in
    --no-voice) RUN_VOICE=false ;;
    --no-auth) RUN_AUTH=false ;;
  esac
done

LOG_DIR="./.run-local-logs"
mkdir -p "$LOG_DIR"

PIDS=()
NAMES=()

echo "Building solution first (avoids concurrent-build file locks on Shared.Contracts)..."
dotnet build Sesver.sln --nologo -v quiet
dotnet build PresenceService/PresenceService.csproj --nologo -v quiet
echo "Build OK."
echo ""

start_dotnet() {
  local name="$1"
  local project="$2"
  local url="$3"
  echo "Starting $name on $url..."
  (
    cd "$(dirname "$project")"
    ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="$url" dotnet run --no-build --no-launch-profile
  ) > "$LOG_DIR/$name.log" 2>&1 &
  PIDS+=($!)
  NAMES+=("$name")
}

cleanup() {
  echo ""
  echo "Stopping all services..."
  for pid in "${PIDS[@]}"; do
    # Negatif PID = process group'un tamamını hedefler (dotnet run / mvnw'nin
    # fork ettiği torun process'ler dahil), tek başına "kill $pid" sadece
    # subshell'i öldürüp asıl uygulamayı arkada yetim bırakırdı.
    kill -TERM -- "-$pid" 2>/dev/null || kill "$pid" 2>/dev/null || true
  done
  sleep 1
  for pid in "${PIDS[@]}"; do
    kill -KILL -- "-$pid" 2>/dev/null || true
  done
  wait 2>/dev/null || true
  echo "Stopped."
}
trap cleanup EXIT INT TERM

start_dotnet "identityservice"       "IdentityService/IdentityService/IdentityService.csproj" "http://localhost:5158"
start_dotnet "clanservice"           "ClanService/ClanService/ClanService.csproj"             "http://localhost:5074"
start_dotnet "messageservice"        "MessageService/MessageService.csproj"                   "http://localhost:5107"
start_dotnet "presenceservice"       "PresenceService/PresenceService.csproj"                 "http://localhost:5241"
start_dotnet "versioncontrolservice" "VersionControlService/VersionControlService.csproj"     "http://localhost:5005"

if [[ "$RUN_AUTH" == true ]]; then
  echo "Starting authenticationservice (Java/Spring Boot)..."
  (
    cd AuthenticationService
    ./mvnw -q spring-boot:run
  ) > "$LOG_DIR/authenticationservice.log" 2>&1 &
  PIDS+=($!)
  NAMES+=("authenticationservice")
fi

echo "Waiting for downstream services before starting the gateway..."
sleep 5
start_dotnet "apigateway" "ApiGateway/ApiGateway.csproj" "http://localhost:5000"

if [[ "$RUN_VOICE" == true ]]; then
  echo "Starting voiceservice..."
  (
    cd VoiceService
    npm run dev
  ) > "$LOG_DIR/voiceservice.log" 2>&1 &
  PIDS+=($!)
  NAMES+=("voiceservice")
fi

echo ""
echo "Tüm servisler başlatıldı. Loglar: $LOG_DIR/<servis>.log"
echo ""
echo "  IdentityService        http://localhost:5158"
echo "  ClanService            http://localhost:5074"
echo "  MessageService         http://localhost:5107"
echo "  PresenceService        http://localhost:5241"
echo "  VersionControlService  http://localhost:5005"
echo "  ApiGateway             http://localhost:5000"
if [[ "$RUN_AUTH" == true ]]; then
  echo "  AuthenticationService  http://localhost:8081 (Java, clan rolleri — auth-db/auth-redis gerektirir)"
fi
if [[ "$RUN_VOICE" == true ]]; then
  echo "  VoiceService           http://localhost:4000 (LiveKit gerektirir, docker-compose.local.yml'de yok)"
fi
echo ""
echo "Durdurmak için Ctrl+C."

for i in "${!PIDS[@]}"; do
  wait "${PIDS[$i]}" || echo "${NAMES[$i]} exited (see $LOG_DIR/${NAMES[$i]}.log)"
done
