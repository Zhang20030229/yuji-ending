interface ConversationSessionState {
  id: string;
  status: "Current" | "Archived";
}

/** URL 指定了会话时必须精确命中，不能把另一段历史误显示在该 URL 下。 */
export function pickActiveConversationSession<T extends ConversationSessionState>(
  sessions: T[],
  requestedSessionId: string | null,
) {
  return requestedSessionId
    ? sessions.find((item) => item.id === requestedSessionId)
    : sessions[0];
}

/** 将新会话放到首位，同时完整保留并归档之前的当前会话。 */
export function mergeCreatedConversationSession<T extends ConversationSessionState>(
  sessions: T[],
  created: T,
): T[] {
  return [
    created,
    ...sessions
      .filter((item) => item.id !== created.id)
      .map((item) => item.status === "Current"
        ? { ...item, status: "Archived" as const } as T
        : item),
  ];
}
