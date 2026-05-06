# Industrial Processing System

Implementacija thread-safe servisa za obradu industrijskih zadataka u C#.

## Struktura projekta

```
IndustrialProcessingSystem/
├── IndustrialProcessingSystem/          # Glavni projekat
│   ├── Models/
│   │   ├── Job.cs                       # Klasa Job
│   │   ├── JobHandle.cs                 # Klasa JobHandle
│   │   └── JobType.cs                   # Enum JobType (Prime, IO)
│   ├── Config/
│   │   └── SystemConfig.cs              # Učitavanje XML konfiguracije
│   ├── Services/
│   │   ├── ProcessingSystem.cs          # Glavni servis (priority queue, workers, eventi)
│   │   ├── JobProcessor.cs              # Logika obrade (Prime, IO)
│   │   ├── JobEventArgs.cs              # Event arguments
│   │   └── ReportService.cs             # Generisanje XML izveštaja
│   ├── Program.cs                       # Ulazna tačka
│   └── SystemConfig.xml                 # Konfiguracioni fajl
│
└── IndustrialProcessingSystem.Tests/    # Unit testovi (xUnit)
    ├── JobTests.cs
    ├── JobProcessorTests.cs
    ├── SystemConfigTests.cs
    └── ProcessingSystemTests.cs
```

## Pokretanje

### Preduslovi
- .NET 8 SDK

### Build i pokretanje
```bash
cd IndustrialProcessingSystem
dotnet run --project IndustrialProcessingSystem
```

### Pokretanje testova
```bash
dotnet test --collect:"XPlat Code Coverage"
```

### Prikaz code coverage-a
```bash
dotnet test --collect:"XPlat Code Coverage" /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
```

## Arhitektura

Sistem je implementiran kao **producer-consumer** sa prioritetima:

- **Producer niti** (broj = WorkerCount iz XML): nasumično dodaju Job-ove u sistem
- **Worker niti** (broj = WorkerCount iz XML): obrađuju poslove iz priority queue-a
- **Priority Queue** (SortedSet): manji broj prioriteta = veći prioritet obrade
- **Idempotentnost**: svaki Job ID može biti obrađen samo jednom
- **MaxQueueSize**: novi poslovi se odbijaju ako je red pun

## Tipovi poslova

### Prime
Računa broj prostih brojeva do zadate vrednosti paralelno.
- Payload format: `numbers:10_000,threads:3`
- Broj niti se ograničava na interval [1, 8]

### IO
Simulira čitanje stanja sa adrese koristeći `Thread.Sleep`.
- Payload format: `delay:1_000` (kašnjenje u ms)
- Vraća nasumičan broj između 0 i 100

## Event sistem

- **JobCompleted**: okida se kada posao uspešno završi
- **JobFailed**: okida se kada posao ne uspe ili bude prekinut (ABORT)
- Svaki događaj asinhrono upisuje log u `job_log.txt`:
  ```
  [2025-05-04 12:00:00.123] [COMPLETED] <JobId>, <Result>
  ```

## Retry logika

- Job koji traje duže od 2 sekunde se smatra **failed**
- Retry se pokušava **2 puta** (ukupno 3 pokušaja)
- Ako i treći put ne uspe → **ABORT** u log fajlu

## Izveštaji

- Generiše se svakih **60 sekundi** u direktorijumu `reports/`
- Čuva se poslednjih **10** izveštaja (kružni bafer, `report_01.xml` ... `report_10.xml`)
- Sadržaj (LINQ): broj završenih poslova po tipu, prosečno vreme, broj neuspešnih po tipu

## XML konfiguracija (SystemConfig.xml)

```xml
<SystemConfig>
  <WorkerCount>5</WorkerCount>
  <MaxQueueSize>100</MaxQueueSize>
  <Jobs>
    <Job Type="Prime" Payload="numbers:10_000,threads:3" Priority="1"/>
    <Job Type="IO" Payload="delay:1_000" Priority="3"/>
  </Jobs>

</SystemConfig>
```
