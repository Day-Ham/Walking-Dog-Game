using System;
using System.Collections.Generic;
using System.Linq;

namespace WalkingDog.Racing
{
    public sealed class DogRaceEntry
    {
        public string Id { get; }
        public string Name { get; }
        public DogStats Stats { get; }
        public DogRaceEntry(string id, string name, DogStats stats)
        {
            if (string.IsNullOrEmpty(id) || stats == null) throw new ArgumentException("A racer needs an ID and stats.");
            stats.Validate(); Id = id; Name = name; Stats = stats.Copy();
        }
    }

    public sealed class DogRaceRunner
    {
        public DogRaceEntry Entry { get; }
        public double Distance { get; internal set; }
        public double Speed { get; internal set; }
        public double FinishTime { get; internal set; } = double.PositiveInfinity;
        public bool Finished => !double.IsPositiveInfinity(FinishTime);
        internal readonly double Form;
        internal DogRaceRunner(DogRaceEntry entry, double form)
        { Entry = new DogRaceEntry(entry.Id, entry.Name, entry.Stats); Form = form; }
    }

    // Pure, deterministic simulation; rendering never decides winners. Every
    // frame advances the same fixed ticks, with interpolated finish crossings.
    public sealed class DogRaceSimulation
    {
        public const double TickSeconds = .05, DefaultLength = 200;
        private double accumulator;
        public double Length { get; }
        public double Elapsed { get; private set; }
        public IReadOnlyList<DogRaceRunner> Runners { get; }
        public bool Completed => Runners.All(r => r.Finished);

        public DogRaceSimulation(IEnumerable<DogRaceEntry> entries, int seed, double length = DefaultLength)
        {
            if (double.IsNaN(length) || double.IsInfinity(length) || length <= 0 || length > 10000)
                throw new ArgumentOutOfRangeException(nameof(length));
            var list = entries?.ToList() ?? throw new ArgumentNullException(nameof(entries));
            if (list.Count < 2 || list.Count > 8 || list.Select(e => e.Id).Distinct().Count() != list.Count)
                throw new ArgumentException("A race needs two to eight distinct dogs.");
            var random = new Random(seed);
            var form = list.OrderBy(e => e.Id, StringComparer.Ordinal).ToDictionary(e => e.Id, e => .98 + random.NextDouble() * .04);
            Runners = list.Select(e => new DogRaceRunner(e, form[e.Id])).ToList().AsReadOnly();
            Length = length;
        }

        public void Advance(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || seconds > 60)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            accumulator += seconds;
            while (accumulator + 1e-9 >= TickSeconds && !Completed)
            {
                accumulator -= TickSeconds;
                foreach (var runner in Runners)
                {
                    if (runner.Finished) continue;
                    var stats = runner.Entry.Stats;
                    double endurance = 8 + stats.stamina * .45;
                    double fatigue = Math.Max(.65, 1 - Math.Max(0, Elapsed - endurance) * .012);
                    double target = (3 + stats.speed * .08) * runner.Form * fatigue;
                    runner.Speed = Math.Min(target, runner.Speed + (.6 + stats.acceleration * .04) * TickSeconds);
                    double leg = runner.Speed * TickSeconds;
                    if (runner.Distance + leg >= Length)
                    {
                        runner.FinishTime = Elapsed + (Length - runner.Distance) / runner.Speed;
                        runner.Distance = Length;
                    }
                    else runner.Distance += leg;
                }
                Elapsed += TickSeconds;
            }
        }

        public List<DogRaceRunner> Standings() => Runners.OrderBy(r => r.FinishTime)
            .ThenByDescending(r => r.Distance).ThenBy(r => r.Entry.Id, StringComparer.Ordinal).ToList();

        public static List<DogRaceEntry> PracticeField(DogStats player) => new List<DogRaceEntry> {
            new DogRaceEntry("player", "Your dog", player),
            new DogRaceEntry("dash", "Dash", new DogStats { speed = 48, stamina = 30, acceleration = 35 }),
            new DogRaceEntry("pip", "Pip", new DogStats { speed = 35, stamina = 48, acceleration = 48 }),
            new DogRaceEntry("scout", "Scout", new DogStats { speed = 42, stamina = 42, acceleration = 42 })
        };
    }
}
