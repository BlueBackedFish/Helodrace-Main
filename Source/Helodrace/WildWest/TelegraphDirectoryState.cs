using System.Collections.Generic;

namespace Helodrace
{
    public sealed class TelegraphDirectoryState
    {
        private readonly HashSet<int> expandedFactions = new HashSet<int>();
        public int? FactionId { get; private set; }
        public TelegraphCompany? Company { get; private set; }
        public bool TabOpen => Company.HasValue;

        public bool IsExpanded(int factionId) => expandedFactions.Contains(factionId);

        public void ToggleFaction(int factionId)
        {
            if (expandedFactions.Remove(factionId))
            {
                if (FactionId == factionId) CloseTab();
            }
            else expandedFactions.Add(factionId);
        }

        public void OpenCompany(int factionId, TelegraphCompany company)
        {
            expandedFactions.Add(factionId);
            FactionId = factionId;
            Company = company;
        }

        public void CloseTab()
        {
            FactionId = null;
            Company = null;
        }

        public void Clear()
        {
            CloseTab();
            expandedFactions.Clear();
        }
    }
}
