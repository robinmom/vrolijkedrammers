namespace Drammers.Worker;

/// <summary>
/// Assembly-marker van de achtergrondverwerking (ADR-007). Workers zijn onafhankelijk van ASP.NET Core,
/// zodat ze later in een apart proces kunnen draaien. Scheduler en outbox volgen in fase 2.
/// </summary>
public static class WorkerModule;
