// ============================================
// MongoDB Initialization Script
// Creates separate databases for each service
// following the Database-per-Service pattern
// ============================================

print('=== Initializing CashFlow databases ===');

// --- Transactions Database ---
db = db.getSiblingDB('transactions_db');

db.createUser({
  user: 'cashflow_app',
  pwd: 'CashFlowApp@2024!',
  roles: [
    { role: 'readWrite', db: 'transactions_db' }
  ]
});

db.createCollection('transactions');
db.transactions.createIndex({ "merchantId": 1, "date": -1 });
db.transactions.createIndex({ "date": -1 });
db.transactions.createIndex({ "type": 1 });
db.transactions.createIndex({ "createdAt": -1 }, { expireAfterSeconds: -1 });

print('✓ transactions_db created with indexes');

// --- Consolidation Database ---
db = db.getSiblingDB('consolidation_db');

db.createUser({
  user: 'cashflow_app',
  pwd: 'CashFlowApp@2024!',
  roles: [
    { role: 'readWrite', db: 'consolidation_db' }
  ]
});

db.createCollection('daily_balances');
db.daily_balances.createIndex({ "merchantId": 1, "date": 1 }, { unique: true });
db.daily_balances.createIndex({ "date": -1 });

db.createCollection('consolidation_events');
db.consolidation_events.createIndex({ "processedAt": -1 });
db.consolidation_events.createIndex({ "status": 1 });

print('✓ consolidation_db created with indexes');
print('=== CashFlow databases initialized ===');
