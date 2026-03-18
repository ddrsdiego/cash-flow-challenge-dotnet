#!/bin/bash
# ============================================
# CashFlow System — Infrastructure Startup
# ============================================

set -e

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

echo "============================================"
echo " CashFlow System — Starting Infrastructure"
echo "============================================"

# Check Docker
if ! command -v docker &> /dev/null; then
    echo -e "${RED}✗ Docker not found. Please install Docker.${NC}"
    exit 1
fi

if ! docker compose version &> /dev/null; then
    echo -e "${RED}✗ Docker Compose V2 not found.${NC}"
    exit 1
fi

echo -e "${GREEN}✓ Docker and Compose found${NC}"

# Check .env
if [ ! -f .env ]; then
    echo -e "${RED}✗ .env file not found. Copy .env.example to .env${NC}"
    exit 1
fi

echo -e "${GREEN}✓ .env file found${NC}"

# Start infrastructure
echo ""
echo -e "${YELLOW}Starting infrastructure services...${NC}"
docker compose up -d

echo ""
echo -e "${YELLOW}Waiting for services to be healthy...${NC}"
sleep 5

# Check health
services=("cashflow-mongodb" "cashflow-redis" "cashflow-rabbitmq" "cashflow-keycloak-db" "cashflow-keycloak" "cashflow-otel-collector" "cashflow-jaeger" "cashflow-prometheus" "cashflow-grafana" "cashflow-seq")

for svc in "${services[@]}"; do
    status=$(docker inspect --format='{{.State.Health.Status}}' "$svc" 2>/dev/null || echo "not found")
    if [ "$status" = "healthy" ]; then
        echo -e "  ${GREEN}✓ $svc${NC}"
    elif [ "$status" = "starting" ]; then
        echo -e "  ${YELLOW}⟳ $svc (starting...)${NC}"
    else
        echo -e "  ${RED}✗ $svc ($status)${NC}"
    fi
done

echo ""
echo "============================================"
echo " Service URLs"
echo "============================================"
echo "  RabbitMQ Management:  http://localhost:15672"
echo "  Keycloak Admin:       http://localhost:8443"
echo "  Jaeger UI:            http://localhost:16686"
echo "  Prometheus:           http://localhost:9090"
echo "  Grafana:              http://localhost:3000"
echo "  Seq (Logs):           http://localhost:8341"
echo "============================================"
echo ""
echo -e "${GREEN}Infrastructure ready!${NC}"
echo "Run 'docker compose --profile app up -d' to start application services."
