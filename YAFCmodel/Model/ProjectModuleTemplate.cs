using System;
using System.Collections.Generic;

namespace YAFC.Model
{
    public class ProjectModuleTemplate : ModelObject<Project>
    {
        public ProjectModuleTemplate(Project owner) : base(owner)
        {
            template = new ModuleTemplate(this);
        }

        [SkipSerialization] public Guid tempGuid { get; set; } 
        public ModuleTemplate template { get; }
        public FactorioObject icon { get; set; }
        public string name { get; set; }
        public List<Entity> filterEntities { get; } = new List<Entity>();
    }
}