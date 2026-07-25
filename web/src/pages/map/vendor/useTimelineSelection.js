import { useCallback, useEffect, useState } from "react";

/**
 * Keeps the selected year/month period synchronized with the native slider.
 */
export function useTimelineSelection(timelineNodes, timelineKey) {
  const [timelineIndex, setTimelineIndex] = useState(
    Math.max(timelineNodes.length - 1, 0),
  );

  useEffect(() => {
    const nextIndex = Math.max(timelineNodes.length - 1, 0);
    setTimelineIndex(nextIndex);
  }, [timelineKey, timelineNodes]);

  const selectPeriod = useCallback(
    (nextIndex) => {
      if (!timelineNodes[nextIndex]) return;
      setTimelineIndex(nextIndex);
    },
    [timelineNodes],
  );

  return { timelineIndex, selectPeriod };
}
