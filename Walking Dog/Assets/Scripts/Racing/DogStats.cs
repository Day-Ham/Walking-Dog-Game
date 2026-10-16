using System;
using System.Collections.Generic;

namespace WalkingDog.Racing
{
    public enum DogStat { Speed, Stamina, Acceleration }

    [Serializable]
    public sealed class DogStats
    {
        public const int StartingValue = 40, MaximumValue = 100, TrainingGain = 5;
        public const long TrainingCost = 20;
        public int speed = StartingValue, stamina = StartingValue, acceleration = StartingValue;
        public int trainingCount;

        // Retrieves the numerical value for the specified dog attribute.
        public int Get(DogStat stat)
        {
            switch (stat)
            {
                case DogStat.Speed: return speed;
                case DogStat.Stamina: return stamina;
                case DogStat.Acceleration: return acceleration;
                default: throw new ArgumentOutOfRangeException(nameof(stat));
            }
        }

        // Creates a deep copy of current dog statistics.
        public DogStats Copy() => new DogStats { speed = speed, stamina = stamina,
            acceleration = acceleration, trainingCount = trainingCount };

        // Increments the chosen attribute by training gain if sufficient points are available and stat is below maximum.
        public DogStats Train(DogStat stat, long points)
        {
            Validate();
            if (Get(stat) >= MaximumValue) throw new InvalidOperationException("This stat is already at its maximum.");
            if (points < TrainingCost) throw new InvalidOperationException("Training needs 20 points.");
            var next = Copy();
            int value = Math.Min(MaximumValue, Get(stat) + TrainingGain);
            switch (stat)
            {
                case DogStat.Speed: next.speed = value; break;
                case DogStat.Stamina: next.stamina = value; break;
                case DogStat.Acceleration: next.acceleration = value; break;
            }
            next.trainingCount++;
            return next;
        }

        // Validates that all attributes and training counts are within acceptable game bounds.
        public void Validate()
        {
            if (speed < 1 || speed > MaximumValue || stamina < 1 || stamina > MaximumValue
                || acceleration < 1 || acceleration > MaximumValue || trainingCount < 0 || trainingCount > 300)
                throw new InvalidOperationException("Invalid dog stats.");
        }

        // Deserializes and validates dog stats from a Firestore dictionary.
        internal static DogStats Parse(IDictionary<string, object> data)
        {
            int Read(string field)
            {
                if (!data.TryGetValue(field, out var value) || !(value is long number) || number < 0 || number > int.MaxValue)
                    throw new InvalidOperationException("Invalid dog profile.");
                return (int)number;
            }
            if (data == null || Read("schemaVersion") != 1) throw new InvalidOperationException("Unsupported dog profile.");
            var stats = new DogStats { speed = Read("speed"), stamina = Read("stamina"),
                acceleration = Read("acceleration"), trainingCount = Read("trainingCount") };
            stats.Validate();
            return stats;
        }

        // Maps a DogStat enum member to its Firestore field key string.
        internal static string Key(DogStat stat)
        {
            switch (stat)
            {
                case DogStat.Speed: return "speed";
                case DogStat.Stamina: return "stamina";
                case DogStat.Acceleration: return "acceleration";
                default: throw new ArgumentOutOfRangeException(nameof(stat));
            }
        }
    }
}
