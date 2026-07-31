using System;

namespace SoflanSupport
{
    internal static class SoflanJudgeOrder
    {
        internal static int GetSiblingIndex(
            int noteIndex,
            int siblingCount,
            Func<int, int?> getSiblingNoteIndex)
        {
            if (siblingCount < 0)
                throw new ArgumentOutOfRangeException(nameof(siblingCount));
            if (getSiblingNoteIndex == null)
                throw new ArgumentNullException(nameof(getSiblingNoteIndex));

            var insertionIndex = 0;
            for (var i = 0; i < siblingCount; i++)
            {
                var siblingNoteIndex = getSiblingNoteIndex(i);
                if (siblingNoteIndex.HasValue && siblingNoteIndex.Value > noteIndex)
                    insertionIndex++;
            }

            return insertionIndex;
        }
    }
}
