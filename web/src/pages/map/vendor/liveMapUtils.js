export function getPeriodNodes(events, granularity) {
  const sorted = [...events].sort(
    (left, right) => new Date(left.occurredAt) - new Date(right.occurredAt),
  );
  const nodes = [];
  let previousPeriod = null;

  sorted.forEach((event) => {
    const date = new Date(event.occurredAt);
    const period = granularity === "year"
      ? `${date.getFullYear()}`
      : `${date.getFullYear()}-${date.getMonth()}`;

    if (period === previousPeriod) {
      nodes[nodes.length - 1] = event;
    } else {
      nodes.push(event);
      previousPeriod = period;
    }
  });

  return nodes;
}

export function isInOrBeforePeriod(event, periodEvent, granularity) {
  if (!periodEvent) return false;

  const eventDate = new Date(event.occurredAt);
  const periodDate = new Date(periodEvent.occurredAt);

  if (granularity === "year") {
    return eventDate.getFullYear() <= periodDate.getFullYear();
  }

  return eventDate.getFullYear() === periodDate.getFullYear()
    && eventDate.getMonth() <= periodDate.getMonth();
}

export function dominantEmotionFamily(families = []) {
  return [...families].sort(
    (left, right) => right.peakIntensity - left.peakIntensity
      || right.count - left.count
      || left.family.localeCompare(right.family, "zh-CN"),
  )[0] ?? null;
}

export function buildCalendarDays(year, month = null, emotionDays = []) {
  if (!year) return [];

  const start = month === null ? new Date(year, 0, 1) : new Date(year, month, 1);
  const totalDays = month === null
    ? (new Date(year, 1, 29).getMonth() === 1 ? 366 : 365)
    : new Date(year, month + 1, 0).getDate();
  const byDate = new Map(emotionDays.map((day) => [day.date, day]));
  const padding = Array.from({ length: start.getDay() }, (_, index) => ({
    key: `padding-${year}-${month ?? "year"}-${index}`,
    isPadding: true,
  }));

  const days = Array.from({ length: totalDays }, (_, index) => {
    const date = month === null
      ? new Date(year, 0, index + 1)
      : new Date(year, month, index + 1);
    const dateKey = [
      date.getFullYear(),
      String(date.getMonth() + 1).padStart(2, "0"),
      String(date.getDate()).padStart(2, "0"),
    ].join("-");
    const emotionDay = byDate.get(dateKey);

    return {
      key: dateKey,
      date: dateKey,
      day: date.getDate(),
      month: date.getMonth(),
      families: emotionDay?.families ?? [],
    };
  });

  return [...padding, ...days];
}

export function buildMonthCalendarDays(year, month, emotionDays = []) {
  if (!year || month === null || month === undefined) return [];

  const first = new Date(year, month, 1);
  const last = new Date(year, month + 1, 0);
  const mondayOffset = (first.getDay() + 6) % 7;
  const totalVisibleDays = Math.ceil((mondayOffset + last.getDate()) / 7) * 7;
  const byDate = new Map(emotionDays.map((day) => [day.date, day]));

  return Array.from({ length: totalVisibleDays }, (_, index) => {
    const date = new Date(year, month, index - mondayOffset + 1);
    const dateKey = [
      date.getFullYear(),
      String(date.getMonth() + 1).padStart(2, "0"),
      String(date.getDate()).padStart(2, "0"),
    ].join("-");
    const emotionDay = byDate.get(dateKey);

    return {
      key: dateKey,
      date: dateKey,
      day: date.getDate(),
      month: date.getMonth(),
      outsideMonth: date.getMonth() !== month,
      families: emotionDay?.families ?? [],
    };
  });
}
