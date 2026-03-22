# Microservices E-Commerce Platform
## Open With: Visual Studio 2022
## File to open: ECommerceMicroservices.sln

---

## Architecture Overview
```
                    ┌─────────────────┐
   All requests ──▶ │   API Gateway   │ :5000  (YARP Reverse Proxy)
                    └────────┬────────┘
              ┌──────────────┼──────────────┐
              ▼              ▼              ▼
       ┌────────────┐ ┌────────────┐ ┌─────────────┐
       │OrderService│ │ProductSvc  │ │PaymentService│
       │  :5001     │ │  :5002     │ │   :5003      │
       └────────────┘ └────────────┘ └─────────────┘
              │              │              │
              └──────────────┴──────────────┘
                        RabbitMQ (message broker)
```

---

## How to Run

### Option A — Visual Studio 2022 (Recommended)
1. Open `ECommerceMicroservices.sln`
2. Right-click Solution → **Set Startup Projects** → Multiple startup projects
3. Set **ApiGateway**, **OrderService**, **ProductService**, **PaymentService** all to **Start**
4. Press **F5**

Each service will launch on its own port automatically.

### Option B — Four separate terminals
```bash
# Terminal 1 — API Gateway (entry point for all requests)
cd ApiGateway && dotnet run
# Runs on http://localhost:5000

# Terminal 2 — Order Service
cd OrderService && dotnet run
# Runs on http://localhost:5001

# Terminal 3 — Product Service
cd ProductService && dotnet run
# Runs on http://localhost:5002

# Terminal 4 — Payment Service
cd PaymentService && dotnet run
# Runs on http://localhost:5003
```

### Option C — Docker Compose (requires Docker Desktop)
```bash
docker-compose up --build
```

---

## Testing the Services

### Via the API Gateway (port 5000 — use this in production)
- GET  http://localhost:5000/api/products
- POST http://localhost:5000/api/orders
- POST http://localhost:5000/api/payments/process

### Direct service Swagger UIs (for development)
- Orders:   http://localhost:5001/swagger
- Products: http://localhost:5002/swagger
- Payments: http://localhost:5003/swagger

### Sample: Create an order
```bash
curl -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "items": [
      {
        "productId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "productName": "Laptop Pro",
        "quantity": 1,
        "unitPrice": 15999.99
      }
    ]
  }'
```

---

## Project Structure
```
BuildingBlocks/EventBus/   — Shared IntegrationEvent base classes
ApiGateway/                — YARP reverse proxy, routes all traffic
OrderService/              — Order creation + Saga state machine
ProductService/            — Product catalogue + stock management
PaymentService/            — Payment processing + refunds
docker-compose.yml         — Full stack in one command
```
