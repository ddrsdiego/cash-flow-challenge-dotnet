# Entendimento — Sessão 06: Propagação de UserId nos Documentos de Arquitetura

## Contexto

Os documentos de arquitetura foram criados sem incluir o campo `userId` nos
modelos de dados, eventos e contratos do sistema. Como o Keycloak está
configurado e autenticação via JWT é obrigatória, toda operação do comerciante
possui um usuário identificado — essa informação precisa ser refletida na
documentação para garantir consistência e rastreabilidade de auditoria.

A ausência do `userId` nos modelos representa uma inconsistência arquitetural:
os requisitos de segurança definem RBAC com roles por usuário, os logs
estruturados já referenciam `userId`, mas o modelo de dados da `Transaction`
não registra quem criou cada lançamento.

## Tarefa

Revisar os documentos de arquitetura existentes e criar um novo ADR para
formalizar como o `userId` é extraído do JWT e propagado pelo sistema.

## Escopo

### Documentos a atualizar

| Documento | Alteração |
|-----------|-----------|
| `docs/architecture/05-domain-mapping.md` | Adicionar `userId` ao modelo Transaction + payload do evento TransactionCreated |
| `docs/architecture/03-component-transactions.md` | Adicionar `userId` ao domain aggregate Transaction |
| `docs/architecture/02-container-diagram.md` | Adicionar `userId` ao schema MongoDB da collection transactions |
| `docs/requirements/01-functional-requirements.md` | Adicionar `userId` nos fluxos UC-01/UC-02 e response bodies |
| `docs/plano-implementacao.md` | Registrar ADR-003 na lista de ADRs planejados |

### Novo documento a criar

- `docs/decisions/ADR-003-user-context-propagation.md`

### Fora do escopo

- Alterações em código (ainda não existe código de aplicação)
- Multi-tenancy (isolamento de dados por usuário)
- Qualquer mudança em regras de negócio existentes
- `DailyConsolidation` NÃO recebe `userId` — o consolidado é global no MVP (single-tenant)

## Decisões de Design

### 1. userId não é informado pelo cliente — é extraído do JWT

O `userId` é extraído automaticamente do JWT pelo middleware de autenticação
no API Gateway e repassado como claim para os serviços downstream. O cliente
**jamais** pode informar um `userId` diferente do que está no token — isso seria
uma falha de segurança.

### 2. userId é auditoria, não isolamento

No MVP (single-tenant), o `userId` identifica **quem criou** cada lançamento
para fins de auditoria e compliance — não isola dados entre diferentes usuários.
Toda consulta retorna todas as transações do comerciante (não filtradas por usuário).

### 3. userId flui pelo evento TransactionCreated

Para manter rastreabilidade completa, o `userId` é incluído no payload do
evento `TransactionCreated`. Futuros consumidores (notificações, auditoria,
analytics) poderão identificar o autor do lançamento sem precisar acessar
outro banco de dados.

### 4. DailyConsolidation permanece sem userId

O saldo diário consolida todas as transações do comerciante, independentemente
de qual usuário as criou. No MVP, não faz sentido segmentar o consolidado por
usuário. Esta decisão é documentada explicitamente no ADR-003 para evitar
ambiguidade.

## Critérios de Aceite

- [ ] Todos os modelos `Transaction` (em todos os documentos) incluem `userId`
- [ ] Evento `TransactionCreated` inclui `userId` no payload
- [ ] `DailyConsolidation` explicitamente documentado como sem `userId`
- [ ] ADR-003 criado e referenciado nos ADRs relacionados (ADR-001, ADR-002)
- [ ] Sem breaking changes nas regras de negócio existentes
- [ ] Consistência entre todos os documentos (mesmo modelo em todos os lugares)

## Referências

- `docs/decisions/ADR-003-user-context-propagation.md` (novo)
- `docs/decisions/ADR-001-async-communication.md` (relacionado)
- `docs/decisions/ADR-002-database-per-service.md` (relacionado)
- `docs/requirements/02-non-functional-requirements.md` — Seção 3 (Segurança) e 4.3 (Logs Estruturados)
