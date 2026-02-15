using System;
using System.Collections.Generic;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Age bracket for population cohorts.
    /// </summary>
    public enum AgeBracket
    {
        Young,      // Children, not working
        Working,    // Working age adults
        Elderly     // Retired, not working
    }

    /// <summary>
    /// Skill level for working population.
    /// </summary>
    public enum SkillLevel
    {
        None,       // Young/elderly - not applicable
        Unskilled,  // Laborers - can do extraction, basic work
        Skilled     // Craftsmen - can do processing, manufacturing
    }

    /// <summary>
    /// Social estate for population cohorts.
    /// </summary>
    public enum Estate
    {
        Commoners,  // Peasants, burghers, laborers, craftsmen
        Clergy,     // Church officials, monks, priests
        Nobility    // Lords, knights, landed gentry
    }

    /// <summary>
    /// A population cohort within a county.
    /// </summary>
    [Serializable]
    public struct PopulationCohort
    {
        public AgeBracket Age;
        public SkillLevel Skill;
        public Estate Estate;
        public int Count;

        public PopulationCohort(AgeBracket age, SkillLevel skill, int count, Estate estate = Estate.Commoners)
        {
            Age = age;
            Skill = skill;
            Estate = estate;
            Count = count;
        }

        public bool CanWork => Age == AgeBracket.Working;
        public bool CanDoUnskilled => CanWork && Estate == Estate.Commoners && (Skill == SkillLevel.Unskilled || Skill == SkillLevel.Skilled);
        public bool CanDoSkilled => CanWork && Estate == Estate.Commoners && Skill == SkillLevel.Skilled;
    }

    /// <summary>
    /// Population state for a county. Contains all cohorts and tracks employment.
    /// </summary>
    [Serializable]
    public class CountyPopulation
    {
        public List<PopulationCohort> Cohorts = new List<PopulationCohort>();

        /// <summary>Number of unskilled workers currently employed.</summary>
        public int EmployedUnskilled;

        /// <summary>Number of skilled workers currently employed.</summary>
        public int EmployedSkilled;

        /// <summary>Total population across all cohorts.</summary>
        public int Total
        {
            get
            {
                int sum = 0;
                foreach (var c in Cohorts) sum += c.Count;
                return sum;
            }
        }

        /// <summary>Total working-age population.</summary>
        public int WorkingAge
        {
            get
            {
                int sum = 0;
                foreach (var c in Cohorts)
                {
                    if (c.CanWork) sum += c.Count;
                }
                return sum;
            }
        }

        /// <summary>Total unskilled workers available.</summary>
        public int TotalUnskilled
        {
            get
            {
                int sum = 0;
                foreach (var c in Cohorts)
                {
                    if (c.CanDoUnskilled && c.Skill == SkillLevel.Unskilled) sum += c.Count;
                }
                return sum;
            }
        }

        /// <summary>Total skilled workers available.</summary>
        public int TotalSkilled
        {
            get
            {
                int sum = 0;
                foreach (var c in Cohorts)
                {
                    if (c.CanDoSkilled) sum += c.Count;
                }
                return sum;
            }
        }

        /// <summary>Unskilled workers not currently employed.</summary>
        public int IdleUnskilled => Math.Max(0, TotalUnskilled - EmployedUnskilled);

        /// <summary>Skilled workers not currently employed.</summary>
        public int IdleSkilled => Math.Max(0, TotalSkilled - EmployedSkilled);

        /// <summary>Population count for a given estate.</summary>
        public int GetEstatePopulation(Estate estate)
        {
            int sum = 0;
            foreach (var c in Cohorts)
            {
                if (c.Estate == estate) sum += c.Count;
            }
            return sum;
        }

        /// <summary>
        /// Try to allocate workers for a facility.
        /// Returns the number of workers actually allocated.
        /// </summary>
        public int AllocateWorkers(LaborType type, int requested)
        {
            if (type == LaborType.Unskilled)
            {
                int available = IdleUnskilled;
                int allocated = Math.Min(available, requested);
                EmployedUnskilled += allocated;
                return allocated;
            }
            else
            {
                int available = IdleSkilled;
                int allocated = Math.Min(available, requested);
                EmployedSkilled += allocated;
                return allocated;
            }
        }

        /// <summary>Reset employment counts (called at start of each tick).</summary>
        public void ResetEmployment()
        {
            EmployedUnskilled = 0;
            EmployedSkilled = 0;
        }

        /// <summary>
        /// Initialize from a raw population count.
        /// Uses reasonable defaults for age/skill distribution.
        /// </summary>
        public static CountyPopulation FromTotal(float totalPopulation)
        {
            // Ensure minimum population so every county can have some workers
            int total = Math.Max(50, (int)totalPopulation);
            var pop = new CountyPopulation();

            // Carve out clergy (~2%) and nobility (~3%) as working-age, SkillLevel.None
            int clergy = (int)(total * 0.02f);
            int nobility = (int)(total * 0.03f);
            int commoners = total - clergy - nobility;

            // Age distribution of commoners: ~20% young, ~65% working, ~15% elderly
            int young = (int)(commoners * 0.20f);
            int elderly = (int)(commoners * 0.15f);
            int working = commoners - young - elderly;

            // Skill distribution of working commoners: ~70% unskilled, ~30% skilled
            // (increased skilled ratio so processing facilities can staff)
            int skilled = (int)(working * 0.30f);
            int unskilled = working - skilled;

            pop.Cohorts.Add(new PopulationCohort(AgeBracket.Young, SkillLevel.None, young));
            pop.Cohorts.Add(new PopulationCohort(AgeBracket.Working, SkillLevel.Unskilled, unskilled));
            pop.Cohorts.Add(new PopulationCohort(AgeBracket.Working, SkillLevel.Skilled, skilled));
            pop.Cohorts.Add(new PopulationCohort(AgeBracket.Elderly, SkillLevel.None, elderly));
            pop.Cohorts.Add(new PopulationCohort(AgeBracket.Working, SkillLevel.None, clergy, Estate.Clergy));
            pop.Cohorts.Add(new PopulationCohort(AgeBracket.Working, SkillLevel.None, nobility, Estate.Nobility));

            return pop;
        }
    }
}
