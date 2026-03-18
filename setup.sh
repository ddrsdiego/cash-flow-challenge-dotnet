#!/bin/bash
# ============================================
# CashFlow System — Setup & Validation
# Execute na raiz do projeto (onde está o docker-compose.yml)
# ============================================

set -e

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

echo "============================================"
echo " CashFlow System — Setup & Validation"
echo "============================================"
echo ""

# --- 1. Verificar pré-requisitos ---
echo -e "${YELLOW}[1/5] Verificando pré-requisitos...${NC}"

if ! command -v docker &> /dev/null; then
    echo -e "${RED}✗ Docker não encontrado. Instale o Docker Desktop.${NC}"
    exit 1
fi
echo -e "${GREEN}  ✓ Docker encontrado${NC}"

if ! docker compose version &> /dev/null; then
    echo -e "${RED}✗ Docker Compose V2 não encontrado.${NC}"
    exit 1
fi
echo -e "${GREEN}  ✓ Docker Compose V2 encontrado${NC}"

if ! docker info &> /dev/null 2>&1; then
    echo -e "${RED}✗ Docker daemon não está rodando. Inicie o Docker Desktop.${NC}"
    exit 1
fi
echo -e "${GREEN}  ✓ Docker daemon rodando${NC}"

# --- 2. Criar estrutura de diretórios ---
echo ""
echo -e "${YELLOW}[2/5] Criando estrutura de diretórios...${NC}"

directories=(
    "infra/config/keycloak"
    "infra/config/otel"
    "infra/config/prometheus"
    "infra/config/grafana/provisioning/datasources"
    "infra/config/grafana/provisioning/dashboards"
    "infra/scripts"
    "docs/architecture"
    "docs/security"
    "docs/decisions"
    "docs/operations"
    "docs/requirements"
    "src"
    "tests"
)

for dir in "${directories[@]}"; do
    mkdir -p "$dir"
    echo -e "${GREEN}  ✓ $dir${NC}"
done

# --- 3. Verificar .env ---
echo ""
echo -e "${YELLOW}[3/5] Verificando configuração...${NC}"

if [ ! -f .env ]; then
    if [ -f .env.example ]; then
        cp .env.example .env
        echo -e "${GREEN}  ✓ .env criado a partir do .env.example${NC}"
    else
        echo -e "${RED}  ✗ .env e .env.example não encontrados!${NC}"
        exit 1
    fi
else
    echo -e "${GREEN}  ✓ .env encontrado${NC}"
fi

if [ ! -f docker-compose.yml ]; then
    echo -e "${RED}  ✗ docker-compose.yml não encontrado!${NC}"
    exit 1
fi
echo -e "${GREEN}  ✓ docker-compose.yml encontrado${NC}"

# --- 4. Verificar arquivos de configuração ---
echo ""
echo -e "${YELLOW}[4/5] Verificando arquivos de configuração...${NC}"

config_files=(
    "infra/config/keycloak/cashflow-realm.json"
    "infra/config/otel/otel-collector-config.yml"
    "infra/config/prometheus/prometheus.yml"
    "infra/config/grafana/provisioning/datasources/datasources.yml"
)

all_ok=true
for file in "${config_files[@]}"; do
    if [ -f "$file" ]; then
        echo -e "${GREEN}  ✓ $file${NC}"
    else
        echo -e "${RED}  ✗ $file — FALTANDO!${NC}"
        all_ok=false
    fi
done

if [ "$all_ok" = false ]; then
    echo ""
    echo -e "${RED}Arquivos de configuração faltando!${NC}"
    echo -e "${RED}Copie os arquivos gerados para as pastas indicadas acima.${NC}"
    exit 1
fi

# --- 5. Validar docker-compose ---
echo ""
echo -e "${YELLOW}[5/5] Validando docker-compose.yml...${NC}"

if docker compose config --quiet 2>/dev/null; then
    echo -e "${GREEN}  ✓ docker-compose.yml válido${NC}"
else
    echo -e "${RED}  ✗ docker-compose.yml com erros. Execute 'docker compose config' para detalhes.${NC}"
    exit 1
fi

echo ""
echo "============================================"
echo -e "${GREEN} Setup completo! Tudo pronto para subir.${NC}"
echo "============================================"
echo ""
echo " Para subir a infraestrutura:"
echo "   docker compose up -d"
echo ""
echo " Para acompanhar os logs:"
echo "   docker compose logs -f"
echo ""
echo " Para verificar o status:"
echo "   docker compose ps"
echo ""
echo " Service URLs (após healthy):"
echo "   RabbitMQ Management:  http://localhost:15672"
echo "   Keycloak Admin:       http://localhost:8443"
echo "   Jaeger UI:            http://localhost:16686"
echo "   Prometheus:           http://localhost:9090"
echo "   Grafana:              http://localhost:3000"
echo "   Seq (Logs):           http://localhost:8341"
echo "============================================"