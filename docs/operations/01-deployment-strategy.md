# 01 — Estratégia de Deploy

## Visão Geral

Este documento descreve a estratégia de deploy do CashFlow System, abrangendo o ambiente de desenvolvimento local (MVP com Docker Compose) e o caminho de evolução para um ambiente de produção em Kubernetes.

A estratégia de deploy é parte integrante da arquitetura — decisões de empacotamento, orquestração e ciclo de vida dos serviços têm impacto direto na disponibilidade, na velocidade de entrega e na capacidade de rollback.

---

## Ambientes

| Ambiente | Infraestrutura | Propósito |
|----------|---------------|-----------|
| **Local / Dev** | Docker Compose | Desenvolvimento, demonstração, testes de integração |
| **Staging** | Kubernetes (cloud ou on-prem) | Validação pré-produção; réplica da produção |
| **Produção** | Kubernetes | Disponibilidade, escalabilidade, SLA |

---

## Empacotamento: Containers com Multi-Stage Build

Todos os serviços são empacotados como containers Docker com **multi-stage build**, separando o ambiente de compilação do artefato final:

```
┌─────────────────────────────────────────────────────────┐
│  MULTI-STAGE BUILD                                      │
│                                                         │
│  Stage 1: Build                                         │
│  → Imagem SDK .NET 8 (maior — inclui compilador)        │
│  → Restaura dependências                                │
│  → Compila e publica o artefato                         │
│                                                         │
│  Stage 2: Runtime                                       │
│  → Imagem Runtime .NET 8 (menor — sem compilador)       │
│  → Copia apenas o artefato compilado                    │
│  → Resultado: imagem final ~80% menor                  │
└─────────────────────────────────────────────────────────┘
```

### Benefícios

- **Segurança:** A imagem final não contém o SDK, ferramentas de build ou código-fonte — reduz a superfície de ataque
- **Tamanho:** Imagem de runtime significativamente menor que a de build
- **Imutabilidade:** Cada build gera um artefato versionado e imutável — o mesmo container promovido de staging vai para produção

---

## Deploy Local: Docker Compose

O `docker-compose.yml` orquestra todos os componentes do sistema em um único comando:

### Componentes Orquestrados

| Serviço | Responsabilidade | Porta Exposta |
|---------|-----------------|---------------|
| API Gateway (YARP) | Ponto de entrada único | 8080 |
| Transactions API | Lançamentos financeiros | Apenas interno |
| Consolidation API | Saldo consolidado | Apenas interno |
| Consolidation Worker | Processamento assíncrono | Nenhuma |
| MongoDB | Persistência | Apenas interno |
| Redis | Cache | Apenas interno |
| RabbitMQ | Mensageria | 15672 (management UI) |
| Keycloak | Identity Provider | 8443 |
| OTel Collector | Coleta de observabilidade | Apenas interno |
| Jaeger | Traces | 16686 (UI) |
| Prometheus | Métricas | 9090 |
| Grafana | Dashboards | 3000 |
| Seq | Logs | 8341 (UI) |

### Health Checks e Dependências

Cada serviço declara suas dependências e condições de saúde. O Docker Compose respeita a ordem de inicialização:

```
MongoDB ──────────────────────────────────────────►
RabbitMQ ─────────────────────────────────────────►
Redis ─────────────────────────────────────────────►
Keycloak (após PostgreSQL) ───────────────────────►
                                                    Transactions API ──►
                                                    Consolidation API ─►
                                                    Worker ────────────►
                                                                         API Gateway ──►
```

Health checks garantem que um serviço só recebe tráfego quando está efetivamente operacional — não apenas quando o processo foi iniciado. Isso previne erros de inicialização por dependência ainda não disponível.

---

## CI/CD Pipeline

O pipeline de entrega contínua garante que nenhum código não validado chegue à produção.

### Estágios do Pipeline

```
┌─────────────────────────────────────────────────────────────────────┐
│                         CI/CD PIPELINE                              │
├──────────┬──────────┬──────────┬──────────┬──────────┬──────────────┤
│  Build   │   Test   │  Scan    │  Package │  Deploy  │   Validate   │
│          │          │          │          │          │              │
│ Compila  │ Testes   │ SAST     │ Docker   │ Deploy   │ Smoke tests  │
│ solução  │ unitários│ análise  │ image    │ staging  │ em staging   │
│          │          │ de       │ + push   │          │              │
│          │ Testes   │ segurança│ para     │ Deploy   │ Rollback     │
│          │ integr.  │ (deps)   │ registry │ produção │ automático   │
│          │          │          │          │ (manual  │ em falha     │
│          │          │          │          │ approval)│              │
└──────────┴──────────┴──────────┴──────────┴──────────┴──────────────┘
```

### Regras de Proteção de Branch

| Branch | Proteção | Deploy |
|--------|---------|--------|
| `main` | PR obrigatório + CI verde + 1 aprovação | Produção (manual) |
| `staging` | CI verde | Staging (automático) |
| `feature/*` | CI verde | Nenhum |

### Variáveis de Ambiente por Ambiente

Secrets e configurações sensíveis nunca são versionados. A estratégia de gestão evolui conforme o ambiente:

| Ambiente | Mecanismo | Exemplos |
|----------|-----------|---------|
| Local | Arquivo `.env` (não versionado) | Senhas de dev, chaves locais |
| Staging | Secrets do CI/CD (GitHub Actions Secrets) | Conexões de staging |
| Produção | Vault de secrets (AWS Secrets Manager, Azure Key Vault, HashiCorp Vault) | Credenciais de produção, certificados |

---

## Deploy em Produção: Kubernetes

### Estratégia: Blue-Green Deploy

O sistema adota **Blue-Green Deploy** para atualizações de serviços stateless (APIs e Gateway):

```
ANTES DO DEPLOY:
  Blue (produção atual):  Transactions API v1.2 — recebendo 100% do tráfego

DURANTE O DEPLOY:
  Blue:   Transactions API v1.2 — recebendo 100% do tráfego
  Green:  Transactions API v1.3 — iniciando, health checks em validação

APÓS HEALTH CHECKS:
  Blue:   Transactions API v1.2 — em standby (mantido por 15 min)
  Green:  Transactions API v1.3 — recebendo 100% do tráfego

ROLLBACK (se necessário):
  Blue:   Transactions API v1.2 — restore imediato (< 30 segundos)
  Green:  Transactions API v1.3 — descartado
```

**Por que Blue-Green e não Rolling Update?**

- **Zero downtime garantido:** O tráfego só é migrado após o Green estar 100% saudável
- **Rollback instantâneo:** Basta redirecionar o tráfego de volta ao Blue — sem reimplantar
- **Validação completa antes da migração:** Health checks e smoke tests no Green antes de qualquer usuário real ser afetado

### Componentes Stateful (tratamento diferenciado)

| Componente | Estratégia | Justificativa |
|-----------|-----------|--------------|
| MongoDB | Replica Set + Rolling Update incremental | Dados persistidos — rolling update preserva disponibilidade |
| Redis | Replicação primária-réplica + failover | Cache é re-populado automaticamente; tolerante a restart |
| RabbitMQ | Cluster com quorum queues | Mensagens não são perdidas durante atualização do cluster |
| Keycloak | Rolling Update com sessões em banco | Sessões existentes continuam válidas durante atualização |

---

## Rollback

### Critérios Automáticos de Rollback

O pipeline executa rollback automático se, nos primeiros 5 minutos após o deploy:

- Taxa de erros HTTP 5xx > 1% (baseline: < 0.1%)
- Latência p95 > 2× do valor pré-deploy
- Health check do serviço falha consecutivamente por 60 segundos

### Procedimento de Rollback Manual

Para situações não detectadas automaticamente, o rollback manual é executado revertendo a imagem para a versão anterior — disponível no registry de containers com tag da versão anterior.

---

## Checklist de Deploy

### Pré-Deploy
- [ ] CI/CD passou integralmente (build + testes + scan)
- [ ] Smoke tests em staging validados
- [ ] Runbook atualizado se houver mudança de comportamento
- [ ] Janela de deploy comunicada (se impacto esperado)
- [ ] Observabilidade verificada (dashboards visíveis)

### Pós-Deploy
- [ ] Health checks de todos os serviços verdes
- [ ] Métricas de erro e latência normais por 5 minutos
- [ ] DLQ do RabbitMQ com zero mensagens novas
- [ ] Nenhum alerta ativo no Grafana
- [ ] Log de auditoria do deploy registrado

---

## Referências

- ADR-004 (API Gateway): `docs/decisions/ADR-004-api-gateway.md` — YARP como único ponto de entrada
- ADR-006 (Observabilidade): `docs/decisions/ADR-006-observability-stack.md` — instrumentação para validação pós-deploy
- Monitoramento: `docs/operations/02-monitoring-observability.md`
- Recuperação de falhas: `docs/operations/04-disaster-recovery.md`
- Requisito não funcional: `docs/requirements/02-non-functional-requirements.md` — Seções de disponibilidade e SLA
