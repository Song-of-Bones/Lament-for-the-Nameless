#pragma once
#include "STAThread.h"

#include <windows.h>

#include <atomic>
#include <functional>
#include <memory>
#include <mutex>
#include <utility>

// Synchronous STA round trip with a lifetime fence.
//
// Two failure modes this type exists to prevent (MR !453):
//  1. The caller times out and destroys the instance while the queued task is
//     still in the STA queue. A raw HANDLE would already be closed (invalid
//     handle in SetEvent) and a raw `this` would be freed (UAF).
//  2. The task runs, the controller call fails, and the caller has already been
//     told "succeeded" because only the completion event was observed.
//
// SyncCompletion is owned by shared_ptr: the event stays open until BOTH the
// caller and the task have dropped their reference. WorkGate's mutex is the
// object fence: the owner sets alive=false under it, so an in-flight task
// finishes before the owner is freed and a late task sees alive==false and
// touches nothing.
namespace nb
{
    struct SyncCompletion
    {
        HANDLE done = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        std::atomic<bool> abandoned { false };
        std::atomic<bool> ok { false };

        SyncCompletion() = default;
        SyncCompletion(const SyncCompletion&)            = delete;
        SyncCompletion& operator=(const SyncCompletion&) = delete;
        ~SyncCompletion() { if (done) CloseHandle(done); }
    };

    struct WorkGate
    {
        std::mutex m;
        bool alive = true;
    };

    enum class SyncOutcome
    {
        Ok,          // work ran and returned true
        Failed,      // work ran and returned false (or threw)
        Abandoned,   // caller gave up: the operation was NOT performed
    };

    // Runs `work` on the STA thread and blocks until it completes, pumping ONLY
    // posted messages (the channel COM cross-apartment marshaling uses) so a
    // WebView2 call marshaling back to the parent-window thread cannot deadlock,
    // while never re-entering the host's paint/input handling.
    inline SyncOutcome RunOnStaSync(STAThread& sta,
                                    const std::shared_ptr<WorkGate>& gate,
                                    std::function<bool()> work,
                                    DWORD timeoutMs)
    {
        if (!gate || !work) return SyncOutcome::Failed;

        auto completion = std::make_shared<SyncCompletion>();
        if (!completion->done) return SyncOutcome::Failed;

        sta.Post([gate, completion, w = std::move(work)] {
            if (completion->abandoned.load()) return;   // caller gave up: nothing to report
            std::lock_guard<std::mutex> lk(gate->m);
            if (!gate->alive || completion->abandoned.load()) return;

            bool ok = false;
            try { ok = w(); }
            catch (...) { ok = false; }

            completion->ok.store(ok);
            SetEvent(completion->done);
        });

        HANDLE done = completion->done;
        const DWORD start = GetTickCount();
        for (;;)
        {
            const DWORD elapsed = GetTickCount() - start;
            if (elapsed >= timeoutMs)
            {
                completion->abandoned.store(true);
                return SyncOutcome::Abandoned;
            }

            const DWORD wait = MsgWaitForMultipleObjectsEx(
                1, &done, timeoutMs - elapsed,
                QS_POSTMESSAGE | QS_ALLPOSTMESSAGE, 0);

            if (wait == WAIT_OBJECT_0) break;
            if (wait == WAIT_FAILED || wait == WAIT_TIMEOUT)
            {
                completion->abandoned.store(true);
                return SyncOutcome::Abandoned;
            }

            // A posted message arrived: dispatch ONLY posted messages. Paint and
            // input are deliberately untouched — this runs inside window teardown.
            MSG msg;
            while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE | PM_QS_POSTMESSAGE))
            {
                if (msg.message == WM_QUIT)
                {
                    PostQuitMessage(static_cast<int>(msg.wParam));
                    completion->abandoned.store(true);
                    return SyncOutcome::Abandoned;
                }
                TranslateMessage(&msg);
                DispatchMessageW(&msg);
            }
        }

        return completion->ok.load() ? SyncOutcome::Ok : SyncOutcome::Failed;
    }
}