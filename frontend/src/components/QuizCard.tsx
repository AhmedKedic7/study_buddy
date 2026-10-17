"use client";

import { useState } from "react";
import type { Quiz } from "@/lib/api";

export function QuizCard({ quiz }: { quiz: Quiz }) {
  const [index, setIndex] = useState(0);
  const [selected, setSelected] = useState<number | null>(null);
  const [score, setScore] = useState(0);
  const [finished, setFinished] = useState(false);

  const question = quiz.questions[index];
  const answered = selected !== null;
  const isLast = index === quiz.questions.length - 1;

  function choose(option: number) {
    if (answered) return;
    setSelected(option);
    if (option === question.correctIndex) setScore((s) => s + 1);
  }

  function next() {
    if (isLast) {
      setFinished(true);
      return;
    }
    setIndex((i) => i + 1);
    setSelected(null);
  }

  function restart() {
    setIndex(0);
    setSelected(null);
    setScore(0);
    setFinished(false);
  }

  return (
    <div className="mt-3 rounded-xl border border-teal-200 bg-teal-50/60 p-4 dark:border-teal-900 dark:bg-teal-950/30">
      <div className="mb-3 flex items-center justify-between text-xs font-medium uppercase tracking-wide text-teal-700 dark:text-teal-400">
        <span>Quiz · {quiz.topic}</span>
        {!finished && (
          <span>
            {index + 1} / {quiz.questions.length}
          </span>
        )}
      </div>

      {finished ? (
        <div className="text-center">
          <p className="text-2xl font-semibold">
            {score} / {quiz.questions.length}
          </p>
          <p className="mt-1 text-sm text-zinc-600 dark:text-zinc-400">
            {score === quiz.questions.length
              ? "Perfect score! 🎉"
              : "Ask me to explain the ones you missed, or quiz you again."}
          </p>
          <button onClick={restart} className="mt-3 rounded-lg bg-teal-600 px-4 py-2 text-sm font-medium text-white hover:bg-teal-700">
            Retry quiz
          </button>
        </div>
      ) : (
        <>
          <p className="mb-3 font-medium">{question.question}</p>
          <div className="space-y-2">
            {question.options.map((option, i) => {
              let style = "border-zinc-200 bg-white hover:border-teal-400 dark:border-zinc-700 dark:bg-zinc-900";
              if (answered && i === question.correctIndex) style = "border-green-500 bg-green-50 dark:bg-green-950/40";
              else if (answered && i === selected) style = "border-red-500 bg-red-50 dark:bg-red-950/40";
              else if (answered) style = "border-zinc-200 bg-white opacity-60 dark:border-zinc-700 dark:bg-zinc-900";
              return (
                <button
                  key={i}
                  onClick={() => choose(i)}
                  disabled={answered}
                  className={`w-full rounded-lg border px-3 py-2 text-left text-sm transition ${style}`}
                >
                  <span className="mr-2 font-semibold text-zinc-500">{String.fromCharCode(65 + i)}.</span>
                  {option}
                </button>
              );
            })}
          </div>

          {answered && (
            <div className="mt-3 rounded-lg bg-white p-3 text-sm dark:bg-zinc-900">
              <p className="font-medium">{selected === question.correctIndex ? "Correct!" : "Not quite."}</p>
              <p className="mt-1 text-zinc-700 dark:text-zinc-300">{question.explanation}</p>
              <p className="mt-2 text-xs text-zinc-500">Source: {question.source}</p>
              <button onClick={next} className="mt-3 rounded-lg bg-teal-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-teal-700">
                {isLast ? "See score" : "Next question"}
              </button>
            </div>
          )}
        </>
      )}
    </div>
  );
}
