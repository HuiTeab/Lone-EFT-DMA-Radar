using LoneEftDmaRadar.Web.TarkovDev.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Loot
{
    public sealed class QuestItem : LootItem
    {
        public QuestItem(TarkovMarketItem entry, Vector3 position) : base(entry, position)
        {
        }

        public QuestItem(string id, string name, Vector3 position) : base(id, name, position)
        {
        }
    }
}
