using System;

namespace Game.Core
{
    /// <summary>
    /// The workshop's depo, as pure maths: how many items it holds, which of them is worth wearing,
    /// what emptying it pays, and where the next item's identity comes from.
    ///
    /// WHY A DEPO AT ALL. Gear used to be one decision per item and nowhere to put anything: a
    /// craft or a drop was worn or scrapped on the spot (see <see cref="Crafting"/>'s pending cell
    /// and <see cref="SeaCombat"/>'s loot card). That is a fine rule for a fight, where a card is
    /// in the way of the next fight, but it makes a Legendary charm rolled before the charm slot is
    /// worth filling into a coin toss. The depo is the third answer — keep it — and every rule about
    /// keeping it lives here, so a test can assert the lot without a save file or a screen.
    ///
    /// AN ITEM IS ITS ID, NOT ITS ROW. Docs/PORT_BOARD.md §3 learned this the hard way on the
    /// contract board: anything that can be re-cut under the player's finger has to be tapped by
    /// identity, or a refused action lands on whatever took the old row. A depo re-orders itself
    /// every time an item leaves the middle of it, so the id is the only safe handle — the row is
    /// where it happens to be drawn this frame.
    ///
    /// NOTHING HERE KNOWS WHAT IT COSTS TO KEEP. No clock, no wallet, no dice, no save: the service
    /// owns all four, exactly as <see cref="ContractBoard"/> and <see cref="MasterChest"/> do.
    /// </summary>
    public static class GearStash
    {
        /// <summary>No item. Ids start at 1 so a zeroed save row can never address a real one.</summary>
        public const long NoId = 0L;

        /// <summary>
        /// The depo's size when nothing has tuned it — five across by four down, which is the
        /// biggest grid that still draws a readable card on a 1080-wide phone.
        /// </summary>
        public const int DefaultCapacity = 20;

        /// <summary>
        /// The next item's id. Monotonic and never <see cref="NoId"/>, so an id that has left the
        /// depo is never handed to the item that replaced it — the whole point of matching a tap
        /// against an id rather than a row.
        /// </summary>
        public static long NextId(long lastId) => lastId < 1L ? 1L : lastId + 1L;

        /// <summary>How many items the depo can still take. Never negative.</summary>
        public static int FreeSlots(int count, int capacity)
        {
            if (capacity < 0) capacity = 0;
            if (count < 0) count = 0;
            int free = capacity - count;
            return free > 0 ? free : 0;
        }

        /// <summary>Whether one more item fits.</summary>
        public static bool HasRoom(int count, int capacity) => FreeSlots(count, capacity) > 0;

        /// <summary>
        /// Whether wearing <paramref name="candidate"/> would be an improvement on
        /// <paramref name="worn"/> — the arrow the depo draws on a card worth a tap.
        ///
        /// Scored, not graded. A Rare cannon with a salvo can beat an Epic one without, and the
        /// score is the same weighted sum the ship's headline uses (<see cref="SeaCombat.ItemScore"/>),
        /// so an arrow here means the number on the panel really does go up. An empty slot is an
        /// upgrade for anything; an item for the wrong slot is not an upgrade for anything.
        /// </summary>
        public static bool IsUpgrade(in SeaCombat.Item candidate, in SeaCombat.Item worn,
                                     in SeaCombat.Tuning t)
        {
            if (candidate.Grade < 0) return false;
            if (worn.Grade < 0) return true;
            if (candidate.Slot != worn.Slot) return false;
            return SeaCombat.ItemScore(candidate, t) > SeaCombat.ItemScore(worn, t);
        }

        /// <summary>
        /// What emptying the depo pays: hurda out, and the lesson the bench learns, over the first
        /// <paramref name="count"/> grades of <paramref name="grades"/>.
        ///
        /// The same two ladders one item at a time — <see cref="SeaCombat.ScrapFor"/> and
        /// <see cref="Crafting.SalvageXpFor"/> — summed here rather than in the service so the
        /// button can print the total BEFORE it is pressed and be printing the number the press
        /// will actually pay. A grade outside the tables is clamped by those two, not dropped:
        /// a repaired save's odd row still pays something rather than silently paying nothing.
        /// </summary>
        public static long ScrapTotal(int[] grades, int count, out long xp)
        {
            xp = 0L;
            if (grades == null) return 0L;
            if (count > grades.Length) count = grades.Length;
            long scrap = 0L;
            for (int i = 0; i < count; i++)
            {
                int grade = grades[i];
                if (grade < 0) continue;
                scrap += SeaCombat.ScrapFor(grade);
                xp += Crafting.SalvageXpFor(grade);
            }
            return scrap;
        }
    }
}
