using CashFlow.SharedKernel.Application.Utils;
using MediatR;

namespace CashFlow.Consolidation.Worker.Application.UseCases.ProcessConsolidationBatch;

public record ProcessConsolidationBatchCommand(
    string TracerId,
    string BatchId) : IRequest<Response>;
