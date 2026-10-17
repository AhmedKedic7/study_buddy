export const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5000";

export type DocumentStatus = "Processing" | "Ready" | "Failed";

export interface StudyDocument {
  id: string;
  title: string;
  fileName: string;
  status: DocumentStatus;
  error: string | null;
  pageCount: number;
  createdAt: string;
}

export interface SourceRef {
  ref: number;
  documentId: string;
  documentTitle: string;
  page: number;
  snippet: string;
}

export interface QuizQuestion {
  question: string;
  options: string[];
  correctIndex: number;
  explanation: string;
  source: string;
}

export interface Quiz {
  topic: string;
  questions: QuizQuestion[];
}

export type AgentEvent =
  | { type: "step"; tool: string; label: string }
  | { type: "sources"; sources: SourceRef[] }
  | { type: "quiz"; quiz: Quiz }
  | { type: "answer"; text: string }
  | { type: "error"; message: string };

export interface ChatTurn {
  role: "user" | "assistant";
  content: string;
}

export async function fetchDocuments(): Promise<StudyDocument[]> {
  const res = await fetch(`${API_URL}/api/documents`);
  if (!res.ok) throw new Error(`Failed to load documents (${res.status})`);
  return res.json();
}

export async function uploadDocument(file: File): Promise<void> {
  const form = new FormData();
  form.append("file", file);
  const res = await fetch(`${API_URL}/api/documents`, { method: "POST", body: form });
  if (!res.ok) {
    const body = await res.json().catch(() => null);
    throw new Error(body?.error ?? `Upload failed (${res.status})`);
  }
}

export async function deleteDocument(id: string): Promise<void> {
  const res = await fetch(`${API_URL}/api/documents/${id}`, { method: "DELETE" });
  if (!res.ok) throw new Error(`Delete failed (${res.status})`);
}

/** POSTs the conversation and yields agent events as they stream in over SSE. */
export async function* streamChat(
  messages: ChatTurn[],
  documentIds: string[],
  signal?: AbortSignal,
): AsyncGenerator<AgentEvent> {
  const res = await fetch(`${API_URL}/api/chat`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ messages, documentIds: documentIds.length ? documentIds : null }),
    signal,
  });
  if (!res.ok || !res.body) throw new Error(`Chat request failed (${res.status})`);

  const reader = res.body.pipeThrough(new TextDecoderStream()).getReader();
  let buffer = "";
  while (true) {
    const { value, done } = await reader.read();
    if (done) return;
    buffer += value;

    let boundary;
    while ((boundary = buffer.indexOf("\n\n")) !== -1) {
      const frame = buffer.slice(0, boundary);
      buffer = buffer.slice(boundary + 2);
      const data = frame
        .split("\n")
        .filter((line) => line.startsWith("data: "))
        .map((line) => line.slice(6))
        .join("\n");
      if (!data) continue;
      if (data === "[DONE]") return;
      yield JSON.parse(data) as AgentEvent;
    }
  }
}
