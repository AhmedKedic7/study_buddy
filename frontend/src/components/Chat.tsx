"use client";

import { useEffect, useRef, useState } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { streamChat, type ChatTurn, type Quiz, type SourceRef } from "@/lib/api";
import { QuizCard } from "./QuizCard";

interface Step {
  tool: string;
  label: string;
}

interface UIMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  steps: Step[];
  sources: SourceRef[];
  quizzes: Quiz[];
  error?: string;
  pending?: boolean;
}

const SUGGESTIONS = [
  "Summarize the key points of my latest lecture",
  "Quiz me on beta blockers",
  "Compare ACE inhibitors and ARBs in a table",
  "Give me a mnemonic for the cytochrome P450 inhibitors",
];

const STEP_ICONS: Record<string, string> = { search_notes: "🔎", generate_quiz: "📝" };

/**
 * Turns [n] citations into links the markdown renderer shows as source chips.
 * gpt-oss models often cite with lenticular brackets instead (【n】 or 【n†source】), so those count too.
 */
function linkCitations(text: string) {
  return text.replace(/\[(\d+)\](?!\()|【(\d+)(?:†[^】]*)?】/g, (_, square, lenticular) => {
    const n = square ?? lenticular;
    return `[${n}](#src-${n})`;
  });
}

export function Chat({ documentIds, hasDocuments }: { documentIds: string[]; hasDocuments: boolean }) {
  const [messages, setMessages] = useState<UIMessage[]>([]);
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  const bottomRef = useRef<HTMLDivElement>(null);
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  function updateLast(update: (m: UIMessage) => UIMessage) {
    setMessages((prev) => [...prev.slice(0, -1), update(prev[prev.length - 1])]);
  }

  async function send(text: string) {
    if (!text.trim() || busy) return;

    const userMessage: UIMessage = { id: crypto.randomUUID(), role: "user", content: text.trim(), steps: [], sources: [], quizzes: [] };
    const assistantMessage: UIMessage = { id: crypto.randomUUID(), role: "assistant", content: "", steps: [], sources: [], quizzes: [], pending: true };

    // Only text goes back as history; quizzes are summarized so turns stay non-empty.
    const history: ChatTurn[] = [...messages, userMessage]
      .filter((m) => !m.error)
      .map((m) => ({
        role: m.role,
        content: m.content || (m.quizzes.length ? `(Showed a quiz on ${m.quizzes.map((q) => q.topic).join(", ")})` : "(no answer)"),
      }));

    setMessages((prev) => [...prev, userMessage, assistantMessage]);
    setInput("");
    setBusy(true);
    abortRef.current = new AbortController();

    try {
      for await (const event of streamChat(history, documentIds, abortRef.current.signal)) {
        switch (event.type) {
          case "step":
            updateLast((m) => ({ ...m, steps: [...m.steps, { tool: event.tool, label: event.label }] }));
            break;
          case "sources":
            updateLast((m) => ({ ...m, sources: [...m.sources, ...event.sources] }));
            break;
          case "quiz":
            updateLast((m) => ({ ...m, quizzes: [...m.quizzes, event.quiz] }));
            break;
          case "answer":
            updateLast((m) => ({ ...m, content: event.text }));
            break;
          case "error":
            updateLast((m) => ({ ...m, error: event.message }));
            break;
        }
      }
    } catch (e) {
      if (!(e instanceof DOMException && e.name === "AbortError")) {
        updateLast((m) => ({ ...m, error: e instanceof Error ? e.message : "Request failed" }));
      }
    } finally {
      updateLast((m) => ({ ...m, pending: false }));
      setBusy(false);
    }
  }

  return (
    <section className="flex min-h-0 flex-1 flex-col">
      <div className="flex-1 overflow-y-auto px-4 py-6">
        <div className="mx-auto max-w-3xl space-y-6">
          {messages.length === 0 && (
            <div className="pt-12 text-center">
              <h2 className="text-2xl font-semibold">What are we studying today?</h2>
              <p className="mt-2 text-sm text-zinc-500">
                {hasDocuments ? "Ask about your notes or try one of these:" : "Start by uploading a lecture PDF on the left."}
              </p>
              <div className="mt-6 grid gap-2 sm:grid-cols-2">
                {SUGGESTIONS.map((s) => (
                  <button
                    key={s}
                    onClick={() => send(s)}
                    disabled={!hasDocuments}
                    className="rounded-xl border border-zinc-200 p-3 text-left text-sm hover:border-teal-400 disabled:opacity-40 dark:border-zinc-800"
                  >
                    {s}
                  </button>
                ))}
              </div>
            </div>
          )}

          {messages.map((m) =>
            m.role === "user" ? (
              <div key={m.id} className="flex justify-end">
                <p className="max-w-[85%] rounded-2xl bg-teal-600 px-4 py-2 whitespace-pre-wrap text-white">{m.content}</p>
              </div>
            ) : (
              <AssistantMessage key={m.id} message={m} />
            ),
          )}
          <div ref={bottomRef} />
        </div>
      </div>

      <form
        onSubmit={(e) => {
          e.preventDefault();
          send(input);
        }}
        className="border-t border-zinc-200 p-4 dark:border-zinc-800"
      >
        <div className="mx-auto flex max-w-3xl gap-2">
          <textarea
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                send(input);
              }
            }}
            rows={1}
            placeholder="Ask about your notes, or say “quiz me on…”"
            className="flex-1 resize-none rounded-xl border border-zinc-300 bg-transparent px-4 py-3 text-sm outline-none focus:border-teal-500 dark:border-zinc-700"
          />
          {busy ? (
            <button type="button" onClick={() => abortRef.current?.abort()} className="rounded-xl border border-zinc-300 px-4 text-sm dark:border-zinc-700">
              Stop
            </button>
          ) : (
            <button type="submit" disabled={!input.trim()} className="rounded-xl bg-teal-600 px-5 text-sm font-medium text-white hover:bg-teal-700 disabled:opacity-40">
              Send
            </button>
          )}
        </div>
      </form>
    </section>
  );
}

function AssistantMessage({ message }: { message: UIMessage }) {
  const sourceByRef = new Map(message.sources.map((s) => [s.ref, s]));

  return (
    <div className="space-y-2">
      {message.steps.length > 0 && (
        <ul className="space-y-1 text-xs text-zinc-500">
          {message.steps.map((step, i) => (
            <li key={i}>
              {STEP_ICONS[step.tool] ?? "⚙️"} {step.label}
            </li>
          ))}
        </ul>
      )}
      {message.pending && !message.content && <p className="text-sm text-zinc-400">Thinking…</p>}

      {message.content && (
        <div className="prose prose-sm prose-zinc max-w-none dark:prose-invert">
          <ReactMarkdown
            remarkPlugins={[remarkGfm]}
            components={{
              a: ({ href, children }) => {
                const ref = href?.startsWith("#src-") ? Number(href.slice(5)) : null;
                const source = ref ? sourceByRef.get(ref) : undefined;
                if (source) {
                  return (
                    <span
                      title={`${source.documentTitle}, p.${source.page}\n\n${source.snippet}`}
                      className="mx-0.5 cursor-help rounded bg-teal-100 px-1.5 py-0.5 text-xs font-medium text-teal-800 no-underline dark:bg-teal-900 dark:text-teal-200"
                    >
                      {children}
                    </span>
                  );
                }
                return (
                  <a href={href} target="_blank" rel="noreferrer">
                    {children}
                  </a>
                );
              },
            }}
          >
            {linkCitations(message.content)}
          </ReactMarkdown>
        </div>
      )}

      {message.quizzes.map((quiz, i) => (
        <QuizCard key={i} quiz={quiz} />
      ))}

      {message.error && <p className="rounded-lg bg-red-50 p-3 text-sm text-red-700 dark:bg-red-950/40 dark:text-red-300">{message.error}</p>}

      {message.sources.length > 0 && !message.pending && (
        <details className="text-xs text-zinc-500">
          <summary className="cursor-pointer">Sources ({message.sources.length})</summary>
          <ul className="mt-2 space-y-2">
            {message.sources.map((s) => (
              <li key={s.ref} className="rounded-lg border border-zinc-200 p-2 dark:border-zinc-800">
                <span className="font-medium text-zinc-700 dark:text-zinc-300">
                  [{s.ref}] {s.documentTitle}, p.{s.page}
                </span>
                <p className="mt-1">{s.snippet}</p>
              </li>
            ))}
          </ul>
        </details>
      )}
    </div>
  );
}
