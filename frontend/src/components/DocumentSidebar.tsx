"use client";

import { useRef, useState } from "react";
import { showToast } from "nextjs-toast-notify";
import { deleteDocument, uploadDocument, type StudyDocument } from "@/lib/api";

interface Props {
  documents: StudyDocument[];
  selectedIds: string[];
  onToggle: (id: string) => void;
  onChanged: () => void;
}

export function DocumentSidebar({ documents, selectedIds, onToggle, onChanged }: Props) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);
  const [dragging, setDragging] = useState(false);
  async function upload(files: FileList | null) {
    if (!files?.length) return;
    setUploading(true);
    try {
      for (const file of Array.from(files)) await uploadDocument(file);
      showToast.success(files.length > 1 ? `${files.length} documents uploaded` : "Document uploaded");
    } catch (e) {
      showToast.error(e instanceof Error ? e.message : "Upload failed");
    } finally {
      setUploading(false);
      onChanged();
    }
  }

  async function remove(id: string) {
    if (!confirm("Delete this document and its notes index?")) return;
    try {
      await deleteDocument(id);
      showToast.success("Document deleted");
    } catch (e) {
      showToast.error(e instanceof Error ? e.message : "Delete failed");
    }
    onChanged();
  }

  return (
    <aside className="flex w-full flex-col border-b border-zinc-200 bg-zinc-50 md:h-full md:w-80 md:border-r md:border-b-0 dark:border-zinc-800 dark:bg-zinc-950">
      <div className="p-4">
        <h1 className="text-lg font-semibold">Study Buddy</h1>
        <p className="text-xs text-zinc-500">Upload your lecture notes, then ask questions or get quizzed.</p>
      </div>

      <div
        onDragOver={(e) => {
          e.preventDefault();
          setDragging(true);
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={(e) => {
          e.preventDefault();
          setDragging(false);
          upload(e.dataTransfer.files);
        }}
        onClick={() => inputRef.current?.click()}
        className={`mx-4 cursor-pointer rounded-xl border-2 border-dashed p-4 text-center text-sm transition ${
          dragging ? "border-teal-500 bg-teal-50 dark:bg-teal-950/30" : "border-zinc-300 hover:border-teal-400 dark:border-zinc-700"
        }`}
      >
        {uploading ? "Uploading…" : "Drop PDFs here or click to upload"}
        <input
          ref={inputRef}
          type="file"
          accept="application/pdf"
          multiple
          hidden
          onChange={(e) => {
            upload(e.target.files);
            e.target.value = "";
          }}
        />
      </div>

      <div className="mt-4 flex-1 overflow-y-auto px-2 pb-4">
        <p className="px-2 pb-2 text-xs text-zinc-500">
          {selectedIds.length ? `Searching ${selectedIds.length} selected` : "Searching all documents"}
        </p>
        {documents.length === 0 && <p className="px-2 text-sm text-zinc-400">No documents yet.</p>}
        <ul className="space-y-1">
          {documents.map((doc) => (
            <li key={doc.id} className="group flex items-start gap-2 rounded-lg px-2 py-2 hover:bg-zinc-100 dark:hover:bg-zinc-900">
              <input
                type="checkbox"
                className="mt-1 accent-teal-600"
                checked={selectedIds.includes(doc.id)}
                disabled={doc.status !== "Ready"}
                onChange={() => onToggle(doc.id)}
                aria-label={`Limit search to ${doc.title}`}
              />
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium" title={doc.fileName}>
                  {doc.title}
                </p>
                <p className="text-xs text-zinc-500">
                  {doc.status === "Ready" && `${doc.pageCount} pages`}
                  {doc.status === "Processing" && <span className="text-amber-600">Processing…</span>}
                  {doc.status === "Failed" && <span className="text-red-600" title={doc.error ?? ""}>Failed: {doc.error}</span>}
                </p>
              </div>
              <button
                onClick={() => remove(doc.id)}
                className="text-xs text-zinc-400 opacity-0 transition group-hover:opacity-100 hover:text-red-600"
                aria-label={`Delete ${doc.title}`}
              >
                ✕
              </button>
            </li>
          ))}
        </ul>
      </div>
    </aside>
  );
}
