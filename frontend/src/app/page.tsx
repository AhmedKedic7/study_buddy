"use client";

import { useCallback, useEffect, useState } from "react";
import { Chat } from "@/components/Chat";
import { DocumentSidebar } from "@/components/DocumentSidebar";
import { showToast } from "nextjs-toast-notify";
import { fetchDocuments, type StudyDocument } from "@/lib/api";

export default function Home() {
  const [documents, setDocuments] = useState<StudyDocument[]>([]);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);


  const refresh = useCallback(async () => {
    try {
      const docs = await fetchDocuments();
      setDocuments(docs);
      setSelectedIds((ids) => ids.filter((id) => docs.some((d) => d.id === id && d.status === "Ready")));
    } catch {
      showToast.error("Can't reach the API. Is the backend running?");
    }
  }, []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- initial data load
    refresh();
  }, [refresh]);

  // Poll while any document is still being processed.
  const processing = documents.some((d) => d.status === "Processing");
  useEffect(() => {
    if (!processing) return;
    const timer = setInterval(refresh, 2500);
    return () => clearInterval(timer);
  }, [processing, refresh]);

  return (
    <main className="flex h-dvh flex-col md:flex-row">
      <DocumentSidebar
        documents={documents}
        selectedIds={selectedIds}
        onToggle={(id) => setSelectedIds((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]))}
        onChanged={refresh}
      />
      <div className="flex min-h-0 flex-1 flex-col">
        <Chat documentIds={selectedIds} hasDocuments={documents.some((d) => d.status === "Ready")} />
      </div>
    </main>
  );
}
