﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP;
using KSPAchievements;

namespace ContractConfigurator
{
    /// <summary>
    /// ContractRequirement set requirement.  Requirement is met if any child requirement is met.
    /// </summary>
    public class AnyRequirement : ContractRequirement
    {
        public override bool RequirementMet(ConfiguredContract contract)
        {
            bool requirementMet = false;
            foreach (ContractRequirement requirement in childNodes)
            {
                if (requirement.enabled)
                {
                    requirementMet |= requirement.CheckRequirement(contract);

                    if (requirementMet)
                    {
                        return true;
                    }
                }
            }
            return requirementMet;
        }
    }
}
