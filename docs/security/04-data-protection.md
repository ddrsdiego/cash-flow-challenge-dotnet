# 04 — Data Protection

## Visão Geral

Este documento detalha as estratégias de **proteção de dados** do CashFlow System, abrangendo:
- Criptografia em trânsito (TLS/mTLS)
- Criptografia em repouso
- Gestão de secrets e variáveis de ambiente
- Mascaramento de dados sensíveis em logs
- Backup e integridade de dados
- Estratégia de conformidade (LGPD)

Dados financeiros exigem tratamento diferenciado — uma vez expostos, o dano é irreversível. Este documento formaliza os controles que garantem confidencialidade, integridade e disponibilidade dos dados.

---

## 1. Criptografia em Trânsito

### 1.1 Camadas de Comunicação

O sistema possui três tipos de comunicação, cada um com sua estratégia:

```
┌─────────────────────────────────────────────────────────────────┐
│  COMUNICAÇÕES DO SISTEMA                                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  1. Cliente → API Gateway                                       │
│     HTTPS com TLS 1.3 (externo, obrigatório)                    │
│                                                                 │
│  2. API Gateway → Serviços Internos                             │
│     HTTP simples dentro da backend-net (isolamento Docker)      │
│     → Em produção: mTLS via service mesh (Istio/Linkerd)        │
│                                                                 │
│  3. Serviços → Data Stores (MongoDB, Redis, RabbitMQ)           │
│     Dentro da backend-net (rede isolada)                        │
│     → Em produção: TLS nas conexões com os data stores          │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 1.2 TLS 1.3 — Comunicação Externa

**Configuração obrigatória** em todo ambiente de produção:

```
Protocolo: TLS 1.3 (mínimo TLS 1.2 como fallback)
Cipher Suites (TLS 1.3):
  - TLS_AES_256_GCM_SHA384
  - TLS_CHACHA20_POLY1305_SHA256
  - TLS_AES_128_GCM_SHA256

Cipher Suites descartadas:
  - RC4 (quebrado)
  - 3DES (vulnerável ao SWEET32)
  - CBC mode (vulnerável ao BEAST/POODLE)

Certificate:
  - CA: Let's Encrypt (produção) ou CA corporativa
  - Key size: RSA 2048+ ou ECDSA P-256
  - Expiração: 90 dias (Let's Encrypt) com renovação automática
  - HSTS: max-age=31536000; includeSubDomains; preload
```

**Por que TLS 1.3?**
- Remove cipher suites obsoletos e vulneráveis do TLS 1.2
- Handshake em 1-RTT (reduz latência)
- Perfect Forward Secrecy (PFS) obrigatório — comprometer a chave não decripta tráfego passado
- Zero-RTT opcional (mas desabilitado — risco de replay attacks em APIs mutáveis)

### 1.3 Comunicação Interna — MVP vs Produção

#### MVP (Docker Compose)

No MVP, a segurança da comunicação interna é provida por **isolamento de rede Docker**:
- Serviços em `backend-net` não são acessíveis de fora do Docker network
- HTTP simples entre serviços (sem TLS) — aceitável dentro de rede isolada
- Não há risco de interceptação se o host estiver comprometido via rede interna

**Limitação explícita:** Se o host Docker estiver comprometido, comunicação interna pode ser interceptada. Aceitável para MVP; mitigado em produção.

#### Produção (Kubernetes com Service Mesh)

```
Estratégia: mTLS via Istio ou Linkerd (service mesh)

Características:
  - mTLS automático entre todos os pods
  - Certificados rotacionados automaticamente (via cert-manager)
  - Identidade de serviço via SPIFFE/SPIRE
  - Sem necessidade de modificação de código dos serviços
  - Observabilidade de tráfego criptografado via Kiali/Jaeger

Exemplo de política Istio:
```

```yaml
apiVersion: security.istio.io/v1beta1
kind: PeerAuthentication
metadata:
  name: cashflow-mtls-policy
  namespace: cashflow
spec:
  mtls:
    mode: STRICT   # mTLS obrigatório — sem fallback para HTTP
```

**Por que mTLS em produção?**

| Critério | Isolamento de Rede (MVP) | mTLS (Produção) |
|----------|-------------------------|-----------------|
| Autenticação mútua entre serviços | ❌ Não | ✅ Sim |
| Criptografia interna | ❌ Não | ✅ Sim |
| Proteção contra lateral movement | ❌ Parcial | ✅ Alta |
| Complexidade operacional | ✅ Simples | ⚠️ Moderada (service mesh) |
| Custo | ✅ Zero | ⚠️ Overhead de CPU (~1-3%) |

**Decisão:** mTLS é a estratégia-alvo para produção. A diferença de segurança justifica o overhead operacional em ambiente Kubernetes.

### 1.4 TLS nos Data Stores (Produção)

| Data Store | Config Dev | Config Produção |
|-----------|-----------|-----------------|
| MongoDB | Sem TLS (rede isolada) | `tls=true` na connection string; TLS 1.2+ |
| Redis | Sem TLS (rede isolada) | `ssl=True`; TLS 1.2+ |
| RabbitMQ | Sem TLS (rede isolada) | AMQPS (AMQP over TLS) na porta 5671 |
| PostgreSQL (Keycloak) | Sem TLS (rede isolada) | `sslmode=require`; TLS 1.2+ |

---

## 2. Criptografia em Repouso

### 2.1 MongoDB — Dados Financeiros

**Estratégia:**

| Ambiente | Configuração | Justificativa |
|----------|-------------|---------------|
| **Desenvolvimento** | Sem encryption at rest | Simplifica setup local; dados não são reais |
| **Produção** | Encryption at rest habilitada | Protege dados se storage for comprometido |

**Implementação em Produção:**

**Opção A: MongoDB Enterprise — Encryption at Rest Nativa**
```yaml
# mongod.conf
security:
  enableEncryption: true
  encryptionKeyFile: /etc/mongodb/keyfile
  encryptionCipherMode: AES256-CBC
```

**Opção B: Filesystem Encryption (LUKS — Linux)**
```bash
# Criptografa o volume onde MongoDB armazena dados
cryptsetup luksFormat /dev/sdb
cryptsetup luksOpen /dev/sdb mongodb-data
mkfs.ext4 /dev/mapper/mongodb-data
```

**Opção C: Cloud Provider Encryption (Recomendado para nuvem)**
- **AWS:** EBS com CMK (Customer-Managed Key) no KMS
- **Azure:** Azure Disk Encryption com Azure Key Vault
- **GCP:** CMEK (Customer-Managed Encryption Keys)

**Gestão de Chaves:**
```
Chave de dados: Gerada pelo MongoDB / provider
Chave mestra (KEK): Gerenciada pelo KMS / Key Vault
Rotação: Anual (ou após suspeita de comprometimento)
Backup da chave: Separado do backup dos dados (not co-located)
```

### 2.2 Redis — Cache de Consolidados

O Redis armazena apenas **dados de saldo consolidado** — não armazena transações individuais. O risco de exposição é moderado (saldo total, não transações detalhadas).

**Controles:**
- `requirepass` (autenticação obrigatória)
- AOF persistence (appendonly yes) — integridade em restart
- TLS em produção (`tls-port 6380`)
- TTL de 5 minutos (dados expiram automaticamente)

**Nota:** Mesmo sem encryption at rest no Redis, os dados cacheados são temporários (TTL 5min) e menos sensíveis que as transações no MongoDB.

### 2.3 RabbitMQ — Mensagens em Trânsito e em Fila

Mensagens no RabbitMQ têm vida curta (consumidas em segundos em condições normais). O risco de persistência longa é baixo.

**Controles:**
- Autenticação AMQP obrigatória
- Mensagens `durable: true` (sobrevivem a restart, mas ficam no disco temporariamente)
- AMQPS em produção (TLS sobre AMQP)
- DLQ com retenção controlada (mensagens mortas investigadas e replayadas ou deletadas)

### 2.4 Backup e Criptografia de Backup

```
Estratégia de Backup:

MongoDB (transactions_db):
  - Frequência: Diário (full) + Horário (incremental/oplog)
  - Retenção: 7 anos (compliance financeiro)
  - Criptografia: AES-256 com chave separada dos dados
  - Armazenamento: Object storage criptografado (S3, Azure Blob)
  - Verificação: Restore test mensal automatizado

MongoDB (consolidation_db):
  - Frequência: Diário
  - Retenção: Indefinida (dados pequenos ~100KB/mês)
  - Criptografia: Mesma estratégia acima

Redis:
  - Não faz backup (dados são cache temporário; reconstruídos do MongoDB)
  - AOF persistence garante integridade em restarts normais

Verificação de Integridade:
  - Checksum SHA-256 calculado antes de enviar para backup
  - Verificação de checksum após restore
  - Alerta se checksum não confere
```

---

## 3. Gestão de Secrets

### 3.1 Classificação de Secrets

| Tipo | Exemplos | Sensibilidade |
|------|---------|---------------|
| **Credenciais de banco de dados** | MongoDB password, Redis password | 🔴 Alta |
| **Client secret do OAuth** | Keycloak client secret | 🔴 Alta |
| **Chaves de assinatura** | JWT signing key | 🔴 Alta |
| **Credenciais do broker** | RabbitMQ user/password | 🔴 Alta |
| **Chaves de API de terceiros** | Não aplicável no MVP | 🔴 Alta |
| **Configurações não sensíveis** | URLs de serviços, TTL, limites | 🟢 Baixa |

### 3.2 Estratégia por Ambiente

#### Desenvolvimento — .env File

```bash
# .env (NÃO commitar no git — .gitignore obrigatório)

# MongoDB
MONGO_ROOT_USER=admin
MONGO_ROOT_PASSWORD=<senha-segura-gerada>
MONGO_TRANSACTIONS_USER=transactions_svc
MONGO_TRANSACTIONS_PASSWORD=<senha-segura-gerada>
MONGO_CONSOLIDATION_USER=consolidation_svc
MONGO_CONSOLIDATION_PASSWORD=<senha-segura-gerada>

# Redis
REDIS_PASSWORD=<senha-segura-gerada>

# RabbitMQ
RABBITMQ_USER=cashflow
RABBITMQ_PASSWORD=<senha-segura-gerada>

# Keycloak
KEYCLOAK_ADMIN=admin
KEYCLOAK_ADMIN_PASSWORD=<senha-segura-gerada>
KEYCLOAK_CLIENT_SECRET=<cliente-secret-gerado>
KEYCLOAK_POSTGRES_PASSWORD=<senha-segura-gerada>

# Observabilidade
SEQ_API_KEY=<api-key-seq>
```

**Geração de senhas seguras:**
```bash
# Linux/macOS
openssl rand -base64 32

# Windows (PowerShell)
[System.Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

**Regras para .env em desenvolvimento:**
- ✅ `.env` no `.gitignore` — nunca commitar
- ✅ `env.example` no repositório (com valores dummy, sem secrets reais)
- ✅ Senhas com mínimo 32 caracteres aleatórios
- ❌ Nunca usar senhas padrão (`admin`, `password`, `123456`)
- ❌ Nunca compartilhar o `.env` por email ou chat

#### Produção — Secrets Manager

```
Provedores recomendados (por plataforma):
  AWS:    AWS Secrets Manager + IAM Roles (EC2/EKS)
  Azure:  Azure Key Vault + Managed Identity
  GCP:    Secret Manager + Workload Identity
  On-prem: HashiCorp Vault

Padrão de acesso:
  1. Aplicação solicita secret via SDK (não variável de ambiente estática)
  2. Secret Manager retorna valor
  3. Rotação automática sem downtime (suportado pelos providers)
  4. Audit log de todos os acessos a secrets
  5. Alerta em acessos fora do padrão (horário, IP, frequência)
```

**Kubernetes — Secrets (intermediário):**
```yaml
# Para ambientes Kubernetes sem Secrets Manager dedicado
apiVersion: v1
kind: Secret
metadata:
  name: cashflow-secrets
  namespace: cashflow
type: Opaque
data:
  mongo-password: <base64-encoded>
  redis-password: <base64-encoded>
  rabbitmq-password: <base64-encoded>
# Nota: Kubernetes Secrets são base64, não criptografados por padrão
# Usar KMS encryption para etcd em produção
```

### 3.3 Rotação de Secrets

| Secret | Frequência | Processo |
|--------|-----------|---------|
| Senhas de banco de dados | A cada 90 dias | Rotação com zero downtime (blue-green de credenciais) |
| Client secret Keycloak | A cada 180 dias | Regenerar no Keycloak; atualizar no Secrets Manager; deploy |
| Chaves de assinatura JWT | A cada 365 dias (ou após suspeita) | Rotação via JWKS sem downtime (ver seção 5.3 do doc 02) |
| Chaves de backup | A cada 365 dias | Re-encriptar backups com nova chave |

### 3.4 O que NUNCA deve estar no código ou repositório

```
❌ Senhas hardcoded
❌ Connection strings com credenciais
❌ JWT signing keys
❌ API keys de terceiros
❌ Arquivos .env reais (apenas .env.example com valores dummy)
❌ Certificados privados (*.key, *.pfx, *.p12)
❌ Client secrets de OAuth
```

**Verificação automatizada:**
```bash
# Pre-commit hook com git-secrets ou truffleHog
git secrets --install
git secrets --register-aws  # Detecta patterns AWS
truffleHog --regex --entropy=False .  # Detecta secrets por entropia
```

---

## 4. Mascaramento de Dados em Logs

### 4.1 Dados Nunca Logados

| Dado | Razão |
|------|-------|
| Senhas / tokens completos | Segurança óbvia |
| Número de cartão (PAN) | PCI DSS (não aplicável no MVP, mas boas práticas) |
| Dados biométricos | LGPD |
| Secrets / API keys | Segurança |
| JWT completo | Token válido pode ser extraído dos logs |

### 4.2 Dados Logados com Mascaramento Parcial

| Dado | Como Logar | Exemplo |
|------|-----------|---------|
| Email | Mascarar domínio parcialmente | `joao@e***.com` |
| Valores financeiros | Logar sem casas decimais em DEBUG | `amount: 500` (não `500.00`) |
| IDs de usuário | Logar completo (não é PII diretamente) | `userId: 550e8400-...` |
| IP de origem | Logar completo (para segurança) | `ip: 192.168.1.100` |

### 4.3 O que É Seguro Logar

```json
{
  "timestamp": "2024-03-15T15:30:45.123Z",
  "level": "INFO",
  "service": "transactions-api",
  "traceId": "550e8400-e29b-41d4-a716-446655440000",
  "userId": "550e8400-e29b-41d4-a716-446655440001",
  "action": "TransactionCreated",
  "transactionId": "507f1f77bcf86cd799439011",
  "type": "CREDIT",
  "date": "2024-03-15",
  "category": "Sales",
  "duration_ms": 45
}
```

**Nota:** `amount` não está nos logs — para auditoria financeira, consultar o banco de dados diretamente (com acesso controlado), não os logs.

### 4.4 Implementação com Serilog / OpenTelemetry

```csharp
// Destructor customizado para mascarar campos sensíveis
public class SensitiveDataDestructor : IDestructuringPolicy
{
    private static readonly HashSet<string> SensitiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "secret", "token", "authorization",
        "creditCard", "ssn", "taxId"
    };

    public bool TryDestructure(object value, ILogEventPropertyValueFactory factory,
        out LogEventPropertyValue result)
    {
        result = null;
        return false; // Implementação específica por campo
    }
}

// Remover Authorization header dos logs
app.Use(async (context, next) =>
{
    context.Request.Headers.Remove("Authorization"); // Remove antes de logar
    await next();
});
```

---

## 5. Proteção de Dados Específicos por Componente

### 5.1 Transactions API

| Dado | Armazenado | Criptografado | Logado |
|------|-----------|---------------|--------|
| `userId` | ✅ MongoDB | Encryption at rest (prod) | ✅ Sim (para auditoria) |
| `type` (CREDIT/DEBIT) | ✅ MongoDB | Encryption at rest (prod) | ✅ Sim |
| `amount` | ✅ MongoDB | Encryption at rest (prod) | ❌ Não em logs |
| `description` | ✅ MongoDB | Encryption at rest (prod) | ❌ Não (pode conter PII) |
| `category` | ✅ MongoDB | Encryption at rest (prod) | ✅ Sim |
| `date` | ✅ MongoDB | Encryption at rest (prod) | ✅ Sim |

### 5.2 Consolidation Service

| Dado | Armazenado | Criptografado | Logado |
|------|-----------|---------------|--------|
| `date` | ✅ MongoDB | Encryption at rest (prod) | ✅ Sim |
| `totalCredits` | ✅ MongoDB + Redis (TTL) | MongoDB: prod; Redis: sem | ❌ Não em logs |
| `totalDebits` | ✅ MongoDB + Redis (TTL) | MongoDB: prod; Redis: sem | ❌ Não em logs |
| `balance` | ✅ MongoDB + Redis (TTL) | MongoDB: prod; Redis: sem | ❌ Não em logs |
| `transactionCount` | ✅ MongoDB | Encryption at rest (prod) | ✅ Sim |

### 5.3 API Gateway

| Dado | Tratamento |
|------|-----------|
| JWT (Authorization header) | Validado; header **removido** antes de encaminhar para logs |
| X-User-Id | Propagado para serviços downstream; logado para rastreabilidade |
| IP do cliente | Logado para audit trail e rate limiting |
| Request body | **Não logado** pelo Gateway (responsabilidade do serviço) |

---

## 6. Conformidade LGPD

### 6.1 Mapeamento de Dados Pessoais

| Dado Pessoal | Classificação LGPD | Onde Armazenado | Base Legal |
|--------------|-------------------|-----------------|------------|
| `userId` (identifica o usuário) | Dado pessoal | transactions_db | Execução de contrato |
| Email (no Keycloak) | Dado pessoal | keycloak_db | Execução de contrato |
| Historico de lançamentos | Dado pessoal indireto (associado ao userId) | transactions_db | Execução de contrato |

### 6.2 Direitos do Titular (LGPD)

| Direito | Mecanismo de Atendimento |
|---------|-------------------------|
| **Acesso** | `GET /api/v1/transactions` retorna todas as transações do usuário autenticado |
| **Portabilidade** | Exportação em formato JSON via endpoint dedicado (roadmap futuro) |
| **Correção** | Não aplicável — transações financeiras são imutáveis por design (compliance) |
| **Eliminação** | Para dados de usuário no Keycloak (admin); transações: política de retenção 7 anos por compliance financeiro |
| **Informação** | Este documento + Política de Privacidade (roadmap futuro) |

### 6.3 Data Minimization

O sistema coleta apenas dados necessários para o funcionamento:
- ✅ `userId` — necessário para auditoria e rastreabilidade
- ✅ `type`, `amount`, `date`, `category` — core do negócio
- ✅ `description` — fornecida pelo usuário para identificação do lançamento
- ❌ Localização, dispositivo, hábitos de acesso — **não coletados**
- ❌ Dados de cartão, CPF, dados bancários — **fora do escopo**

### 6.4 Retenção de Dados

| Dado | Retenção | Justificativa |
|------|----------|---------------|
| Transações financeiras | 7 anos | Compliance fiscal (obrigação legal) |
| Consolidados diários | Indefinida | Dados pequenos; valor histórico para o negócio |
| Logs de aplicação | 30 dias | Operacional; Seq com rotação automática |
| Logs de auditoria de segurança | 2 anos | Compliance e investigação de incidentes |
| Dados de sessão Keycloak | 10 horas (expiração automática) | Mínimo necessário para sessão do usuário |

---

## 7. Resposta a Incidentes de Segurança de Dados

### 7.1 Classificação de Incidentes

| Severidade | Exemplos | Tempo de Resposta |
|-----------|---------|------------------|
| 🔴 **Crítico** | Vazamento de dados financeiros, acesso não autorizado ao banco | < 1 hora |
| 🟠 **Alto** | Comprometimento de credenciais, secrets expostos | < 4 horas |
| 🟡 **Médio** | Tentativa de acesso não autorizado bloqueada, DLQ crescendo | < 24 horas |
| 🟢 **Baixo** | Anomalia em métricas, aumento de erros 4xx | < 72 horas |

### 7.2 Plano de Resposta (Simplificado)

```
Detecção:
  → Alerta Prometheus/Grafana ou log anômalo no Seq

Contenção:
  → Revogar credenciais comprometidas (Keycloak: logout all sessions)
  → Rotacionar secrets no Secrets Manager
  → Isolar componente afetado (se necessário)

Investigação:
  → Analisar traces no Jaeger (por traceId do incidente)
  → Analisar logs no Seq (filtrar por userId, IP, timestamp)
  → Identificar escopo do comprometimento

Recuperação:
  → Restaurar de backup verificado (com checksum)
  → Deploy de versão corrigida (se bug)
  → Monitoramento intensivo por 24h

Notificação (LGPD Art. 48):
  → Se dados pessoais afetados: notificar ANPD em até 72h
  → Comunicar titulares afetados
  → Documentar incidente e ações tomadas
```

---

## 8. Resumo: Dev vs Produção

| Controle | Desenvolvimento | Produção |
|----------|----------------|---------|
| TLS externo | HTTP (localhost) | TLS 1.3 obrigatório |
| Comunicação interna | HTTP (rede isolada Docker) | mTLS via service mesh |
| Encryption at rest MongoDB | ❌ Desabilitado | ✅ Habilitado (KMS) |
| TLS nos data stores | ❌ Sem TLS (rede isolada) | ✅ TLS 1.2+ |
| Gestão de secrets | `.env` file | Secrets Manager (AWS/Azure/Vault) |
| Backup criptografado | ❌ Sem backup | ✅ AES-256 + checksum |
| HSTS | ❌ Não (HTTP local) | ✅ max-age=31536000 |

**Prioridade de evolução:**
1. TLS nos data stores (alto impacto, baixo custo)
2. Encryption at rest via cloud provider (médio custo)
3. mTLS via service mesh (alto custo operacional, alta segurança)
4. Secrets Manager integrado (médio custo, essencial para produção)

---

## Referências

- [TLS 1.3 — RFC 8446](https://tools.ietf.org/html/rfc8446)
- [OWASP Cryptographic Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Cryptographic_Storage_Cheat_Sheet.html)
- [MongoDB Security Checklist](https://www.mongodb.com/docs/manual/administration/security-checklist/)
- [Redis Security](https://redis.io/docs/management/security/)
- [HashiCorp Vault](https://www.vaultproject.io/)
- [LGPD — Lei 13.709/2018](https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13709.htm)
- [Istio mTLS](https://istio.io/latest/docs/concepts/security/#mutual-tls-authentication)
- `docs/security/01-security-architecture.md` — Arquitetura geral e inventário de controles
- `docs/security/02-authentication-authorization.md` — Rotação de chaves JWT
- `docs/requirements/02-non-functional-requirements.md` — Seções 3.3, 3.4, 7.1, 7.2
