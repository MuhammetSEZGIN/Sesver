#!/usr/bin/env bash
# Tüm Sesver mikroservislerini yerelde (Docker'sız) ayağa kaldırır.
#
# Ön koşul: altyapı servisleri (Postgres x4, MongoDB, RabbitMQ, Redis ve LiveKit) çalışıyor
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

LOCAL_JWT_KEY="${JWT_KEY:-Belirlecek-gizli-anahtar-en-az-32-karakter-olmali!}"
LOCAL_JWT_ISSUER="${JWT_ISSUER:-voxify.identity}"
LOCAL_JWT_AUDIENCE="${JWT_AUDIENCE:-voxify.clients}"
LOCAL_RABBIT_HOST="${RABBITMQ_HOST:-localhost}"
LOCAL_RABBIT_VHOST="${RABBITMQ_VHOST:-/}"
LOCAL_RABBIT_PORT="${RABBITMQ_PORT:-5672}"
LOCAL_RABBIT_USER="${RABBITMQ_USER:-guest}"
LOCAL_RABBIT_PASSWORD="${RABBITMQ_PASSWORD:-guest}"

check_local_infrastructure() {
  if ! command -v docker >/dev/null 2>&1; then
    echo "Docker bulunamadı. Önce yerel altyapıyı çalıştırın."
    exit 1
  fi

  local required=(identity-db clan-db notification-db message-db rabbitmq)
  if [[ "$RUN_AUTH" == true ]]; then
    required+=(auth-db auth-redis)
  fi
  if [[ "$RUN_VOICE" == true ]]; then
    required+=(livekit-redis livekit-server)
  fi

  local running
  running="$(docker compose -f docker-compose.local.yml ps --services --status running 2>/dev/null || true)"
  local missing=()
  for service in "${required[@]}"; do
    if ! grep -qx "$service" <<< "$running"; then
      missing+=("$service")
    fi
  done

  if (( ${#missing[@]} > 0 )); then
    echo "Çalışmayan Docker altyapı servisleri: ${missing[*]}"
    echo "Önce çalıştırın: docker compose -f docker-compose.local.yml up -d"
    exit 1
  fi
}

check_local_infrastructure

echo "Building solution first (avoids concurrent-build file locks on Shared.Contracts)..."
dotnet build Sesver.sln --nologo -v quiet
dotnet build PresenceService/PresenceService.csproj --nologo -v quiet
echo "Build OK."
echo ""

start_dotnet() {
  local name="$1"
  local project="$2"
  local url="$3"
  shift 3
  echo "Starting $name on $url..."
  (
    cd "$(dirname "$project")"
    env ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="$url" "$@" \
      dotnet run --no-build --no-launch-profile
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

start_dotnet "identityservice" "IdentityService/IdentityService/IdentityService.csproj" "http://localhost:5158" \
  "ConnectionStrings__DefaultConnection=Host=localhost;Port=5433;Database=IdentityDb;Username=admin;Password=admin123" \
  "Jwt__Key=$LOCAL_JWT_KEY" "Jwt__Issuer=$LOCAL_JWT_ISSUER" "Jwt__Audience=$LOCAL_JWT_AUDIENCE" \
  "RabbitMq__HostName=$LOCAL_RABBIT_HOST" "RabbitMq__VirtualHost=$LOCAL_RABBIT_VHOST" \
  "RabbitMq__Port=$LOCAL_RABBIT_PORT" "RabbitMq__UserName=$LOCAL_RABBIT_USER" \
  "RabbitMq__Password=$LOCAL_RABBIT_PASSWORD" "Smtp__Enabled=false"

start_dotnet "clanservice" "ClanService/ClanService/ClanService.csproj" "http://localhost:5074" \
  "ConnectionStrings__DefaultConnection=Host=localhost;Port=5432;Database=ClanDb;Username=admin;Password=admin123" \
  "RabbitMQ__HostName=$LOCAL_RABBIT_HOST" "RabbitMQ__VirtualHost=$LOCAL_RABBIT_VHOST" \
  "RabbitMQ__Port=$LOCAL_RABBIT_PORT" "RabbitMQ__UserName=$LOCAL_RABBIT_USER" \
  "RabbitMQ__Password=$LOCAL_RABBIT_PASSWORD"

start_dotnet "messageservice" "MessageService/MessageService.csproj" "http://localhost:5107" \
  "Jwt__Key=$LOCAL_JWT_KEY" "Jwt__Issuer=$LOCAL_JWT_ISSUER" "Jwt__Audience=$LOCAL_JWT_AUDIENCE" \
  "MongoDbConnection__ConnectionString=mongodb://root:1234@localhost:27017" \
  "MongoDbConnection__DatabaseName=messageservicedb" \
  "RabbitMQ__HostName=$LOCAL_RABBIT_HOST" "RabbitMQ__VirtualHost=$LOCAL_RABBIT_VHOST" \
  "RabbitMQ__Port=$LOCAL_RABBIT_PORT" "RabbitMQ__UserName=$LOCAL_RABBIT_USER" \
  "RabbitMQ__Password=$LOCAL_RABBIT_PASSWORD"

start_dotnet "presenceservice" "PresenceService/PresenceService.csproj" "http://localhost:5241" \
  "Jwt__Key=$LOCAL_JWT_KEY" "Jwt__Issuer=$LOCAL_JWT_ISSUER" "Jwt__Audience=$LOCAL_JWT_AUDIENCE" \
  "RabbitMQ__HostName=$LOCAL_RABBIT_HOST" "RabbitMQ__VirtualHost=$LOCAL_RABBIT_VHOST" \
  "RabbitMQ__Port=$LOCAL_RABBIT_PORT" "RabbitMQ__UserName=$LOCAL_RABBIT_USER" \
  "RabbitMQ__Password=$LOCAL_RABBIT_PASSWORD" \
  "Services__IdentityBaseUrl=http://localhost:5158" "Services__MessageBaseUrl=http://localhost:5107"

start_dotnet "notificationservice" "NotificationService/NotificationService.csproj" "http://localhost:5160" \
  "ConnectionStrings__DefaultConnection=Host=localhost;Port=5434;Database=NotificationDb;Username=admin;Password=admin123" \
  "Jwt__Key=$LOCAL_JWT_KEY" "Jwt__Issuer=$LOCAL_JWT_ISSUER" "Jwt__Audience=$LOCAL_JWT_AUDIENCE" \
  "RabbitMQ__HostName=$LOCAL_RABBIT_HOST" "RabbitMQ__VirtualHost=$LOCAL_RABBIT_VHOST" \
  "RabbitMQ__Port=$LOCAL_RABBIT_PORT" "RabbitMQ__UserName=$LOCAL_RABBIT_USER" \
  "RabbitMQ__Password=$LOCAL_RABBIT_PASSWORD" \
  "Cors__AllowedOrigins__0=http://localhost:5173" "Cors__AllowedOrigins__1=http://localhost:3000" \
  "Cors__AllowedOrigins__2=http://localhost:1420" "Cors__AllowedOrigins__3=http://tauri.localhost" \
  "Cors__AllowedOrigins__4=https://tauri.localhost" "Cors__AllowedOrigins__5=tauri://localhost" \
  "Cors__AllowedOrigins__6=http://127.0.0.1:5173" "Cors__AllowedOrigins__7=http://127.0.0.1:3000" \
  "Cors__AllowedOrigins__8=http://127.0.0.1:1420"

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
start_dotnet "apigateway" "ApiGateway/ApiGateway.csproj" "http://localhost:5000" \
  "Jwt__Key=$LOCAL_JWT_KEY" "Jwt__Issuer=$LOCAL_JWT_ISSUER" "Jwt__Audience=$LOCAL_JWT_AUDIENCE" \
  "AuthService__BaseUrl=http://localhost:8081"

if [[ "$RUN_VOICE" == true ]]; then
  echo "Starting voiceservice..."
  (
    cd VoiceService
    env "JWT_KEY=$LOCAL_JWT_KEY" "JWT_ISSUER=$LOCAL_JWT_ISSUER" \
      "JWT_AUDIENCE=$LOCAL_JWT_AUDIENCE" "LIVEKIT_API_KEY=${LIVEKIT_API_KEY:-devkey}" \
      "LIVEKIT_API_SECRET=${LIVEKIT_API_SECRET:-secret}" "LIVEKIT_URL=${LIVEKIT_URL:-ws://localhost:7880}" \
      "MESSAGE_SERVICE_URL=http://localhost:5107" "PORT=4000" npm run dev
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
echo "  NotificationService    http://localhost:5160"
echo "  VersionControlService  http://localhost:5005"
echo "  ApiGateway             http://localhost:5000"
if [[ "$RUN_AUTH" == true ]]; then
  echo "  AuthenticationService  http://localhost:8081 (Java, clan rolleri — auth-db/auth-redis gerektirir)"
fi
if [[ "$RUN_VOICE" == true ]]; then
  echo "  VoiceService           http://localhost:4000 (LiveKit http://localhost:7880)"
fi
echo ""
echo "Durdurmak için Ctrl+C."

for i in "${!PIDS[@]}"; do
  wait "${PIDS[$i]}" || echo "${NAMES[$i]} exited (see $LOG_DIR/${NAMES[$i]}.log)"
done
