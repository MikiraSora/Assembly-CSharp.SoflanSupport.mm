#if DEBUG
using MAI2.Util;
using Manager;
using Monitor;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SoflanSupport
{
    /// <summary>
    /// DEBUG-only NoteGuide lifecycle diagnostics. This class observes pool usage and visual state;
    /// it must not repair, resize, hide, or otherwise mutate Guide objects.
    /// </summary>
    internal static class GuideDiagnostics
    {
        private const float SummaryIntervalSeconds = 1f;

        private static readonly Dictionary<int, NoteGuide> guides = new Dictionary<int, NoteGuide>();
        private static readonly Dictionary<int, int> guidePoolIds = new Dictionary<int, int>();
        private static readonly Dictionary<int, ActiveNoteState> activeNotes = new Dictionary<int, ActiveNoteState>();
        private static readonly Dictionary<int, int> guideOwners = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> controllerPoolIds = new Dictionary<int, int>();
        private static readonly HashSet<int> reportedInactiveEach = new HashSet<int>();
        private static readonly HashSet<int> guidesAwaitingInitialize = new HashSet<int>();
        private static readonly HashSet<int> notesAwaitingInitialize = new HashSet<int>();
        private static readonly Dictionary<int, int> guideEnableCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> noteEnableCounts = new Dictionary<int, int>();
        private static readonly HashSet<int> reportedGuideAwaitingInitialize = new HashSet<int>();
        private static readonly HashSet<int> reportedActiveNoteInactiveGuide = new HashSet<int>();
        private static readonly HashSet<int> reportedActiveNoteDetachedGuide = new HashSet<int>();
        private static readonly HashSet<int> reportedInactiveNoteActiveGuide = new HashSet<int>();
        private static readonly HashSet<int> reportedNoteDisabledWithoutEnd = new HashSet<int>();
        private static readonly HashSet<int> reportedGuideOwnerMismatch = new HashSet<int>();

        private static long eventSequence;
        private static int eachDumpSequence;
        private static int registrationSequence;
        private static int currentRegistration;
        private static int currentControllerId;
        private static bool currentRegistrationTracksGuide;
        private static bool currentRegistrationAssignedGuide;
        private static bool currentRegistrationInitializedNote;
        private static float nextSummaryTime;

        public static void OnFrame()
        {
            var realtime = Time.realtimeSinceStartup;
            if (realtime < nextSummaryTime)
                return;

            nextSummaryTime = realtime + SummaryIntervalSeconds;
            WriteSnapshot("SNAPSHOT");
        }

        public static int DumpVisibleEachGuides()
        {
            var dumpId = ++eachDumpSequence;
            var currentMsec = GetCurrentMsec();
            var candidates = new List<NoteGuide>();
            var visibleCount = 0;
            var residualCount = 0;

            foreach (var pair in guides)
            {
                var guide = pair.Value;
                if (guide == null)
                    continue;

                GetEachActivity(guide, out var activeSelfCount, out var activeInHierarchyCount);
                if (activeSelfCount == 0 && activeInHierarchyCount == 0)
                    continue;

                candidates.Add(guide);
                if (activeInHierarchyCount > 0)
                    visibleCount++;
                if (activeSelfCount > 0 && activeInHierarchyCount == 0)
                    residualCount++;
            }

            Write("EACH-DUMP-BEGIN",
                $"dump={dumpId} currentMsec={currentMsec:F3} trackedGuides={guides.Count} " +
                $"candidates={candidates.Count} visible={visibleCount} residualSelfOnly={residualCount} {DescribeCounts()}");

            for (var i = 0; i < candidates.Count; i++)
            {
                var guide = candidates[i];
                Write("EACH-DUMP-GUIDE", DescribeEachDumpGuide(dumpId, i, guide, currentMsec));
                WriteEachChildren(dumpId, i, guide);
            }

            Write("EACH-DUMP-END",
                $"dump={dumpId} currentMsec={GetCurrentMsec():F3} candidates={candidates.Count} " +
                $"visible={visibleCount} residualSelfOnly={residualCount}");
            return candidates.Count;
        }

        public static void OnRegisterBegin(object controller, NoteData note)
        {
            currentRegistration = ++registrationSequence;
            currentControllerId = GetUnityObjectId(controller);
            currentRegistrationTracksGuide = IsGuideNote(note);
            currentRegistrationAssignedGuide = false;
            currentRegistrationInitializedNote = false;

            if (!currentRegistrationTracksGuide)
                return;

            Write("REG-BEGIN",
                $"reg={currentRegistration} ctrl={currentControllerId} pool={GetCurrentPoolId()} " +
                $"{DescribeNote(note)} {DescribeCurrentPoolCounts()}");
        }

        public static void OnRegisterEnd(NoteData note, bool result)
        {
            if (currentRegistrationTracksGuide)
            {
                var severity = !result
                    ? "NOTE-POOL-EXHAUSTED"
                    : (!currentRegistrationAssignedGuide ? "GUIDE-ASSIGNMENT-MISSING" : "REG-END");
                Write(severity,
                    $"reg={currentRegistration} ctrl={currentControllerId} result={result} " +
                    $"setGuide={currentRegistrationAssignedGuide} initialized={currentRegistrationInitializedNote} " +
                    $"pool={GetCurrentPoolId()} {DescribeNote(note)} {DescribeCurrentPoolCounts()}");
            }

            ClearCurrentRegistration();
        }

        public static void OnRegisterFailed(NoteData note, Exception exception)
        {
            Write("REG-EXCEPTION",
                $"reg={currentRegistration} ctrl={currentControllerId} setGuide={currentRegistrationAssignedGuide} " +
                $"initialized={currentRegistrationInitializedNote} {DescribeNote(note)} " +
                $"pool={GetCurrentPoolId()} exception={exception.GetType().FullName}: {exception.Message} " +
                $"{DescribeCurrentPoolCounts()}");
            ClearCurrentRegistration();
        }

        public static void OnForceCollectBefore(object controller)
        {
            var controllerId = GetUnityObjectId(controller);
            Write("FORCE-COLLECT-BEFORE",
                $"ctrl={controllerId} pool={GetControllerPoolId(controllerId)} " +
                DescribeCounts(GetControllerPoolId(controllerId)));
        }

        public static void OnForceCollectAfter(object controller)
        {
            activeNotes.Clear();
            guideOwners.Clear();
            ClearReportedNoteAnomalies();
            var controllerId = GetUnityObjectId(controller);
            Write("FORCE-COLLECT-AFTER",
                $"ctrl={controllerId} pool={GetControllerPoolId(controllerId)} " +
                DescribeCounts(GetControllerPoolId(controllerId)));
            WriteSnapshot("FORCE-COLLECT-SNAPSHOT");
        }

        public static void OnNoteSetGuide(NoteBase noteObject, NoteGuide guide)
        {
            var noteObjectId = GetUnityObjectId(noteObject);
            var guideId = GetUnityObjectId(guide);
            var poolId = GetGuidePoolId(guide);
            currentRegistrationAssignedGuide = true;

            if (currentControllerId != 0 && poolId != 0)
                controllerPoolIds[currentControllerId] = poolId;

            if (guideId != 0 && guideOwners.TryGetValue(guideId, out var previousOwner)
                && previousOwner != noteObjectId && activeNotes.ContainsKey(previousOwner))
            {
                Write("GUIDE-COLLISION",
                    $"reg={currentRegistration} guide={guideId} previousNoteObject={previousOwner} " +
                    $"previousOwner={DescribeActiveNote(previousOwner)} newNoteObject={noteObjectId} " +
                    $"phase=SetGuideObject {DescribeGuide(guide)}");
            }

            if (guideId != 0)
                guideOwners[guideId] = noteObjectId;

            Write("SET-GUIDE",
                $"reg={currentRegistration} ctrl={currentControllerId} pool={poolId} " +
                $"noteObject={noteObjectId} monitor={noteObject.MonitorId} guide={guideId} {DescribeGuide(guide)}");
        }

        public static void OnNoteInitializeBefore(
            NoteBase noteObject,
            NoteData note,
            bool needGuide,
            NoteGuide guide,
            bool assignedThisCycle,
            int previousNoteIndex)
        {
            if (!needGuide && !IsGuideNote(note))
                return;

            currentRegistrationInitializedNote = true;
            var noteObjectId = GetUnityObjectId(noteObject);
            var guideId = GetUnityObjectId(guide);
            var enableSeen = notesAwaitingInitialize.Contains(noteObjectId);

            if (!enableSeen)
            {
                Write("NOTE-INIT-WITHOUT-ENABLE",
                    $"reg={currentRegistration} noteObject={noteObjectId} monitor={noteObject.MonitorId} " +
                    $"previousNoteIndex={previousNoteIndex} {DescribeNote(note)} {DescribeGuide(guide)}");
            }

            if (needGuide && !assignedThisCycle)
            {
                Write("POOL-EXHAUSTION-SUSPECT",
                    $"reg={currentRegistration} noteObject={noteObjectId} previousNoteIndex={previousNoteIndex} " +
                    $"assignedThisCycle=False guide={guideId} {DescribeNote(note)} {DescribeGuide(guide)} " +
                    $"{DescribeCurrentPoolCounts()}");
            }

            if (guideId != 0 && guideOwners.TryGetValue(guideId, out var previousOwner)
                && previousOwner != noteObjectId && activeNotes.ContainsKey(previousOwner))
            {
                Write("GUIDE-COLLISION",
                    $"reg={currentRegistration} guide={guideId} previousNoteObject={previousOwner} " +
                    $"previousOwner={DescribeActiveNote(previousOwner)} newNoteObject={noteObjectId} " +
                    $"phase=Initialize {DescribeNote(note)} {DescribeGuide(guide)}");
            }

            if (guideId != 0)
                guideOwners[guideId] = noteObjectId;

            activeNotes[noteObjectId] = new ActiveNoteState(
                noteObject,
                note,
                guide,
                note.indexNote,
                guideId,
                GetGuidePoolId(guide),
                needGuide);
            Write("NOTE-INIT-BEFORE",
                $"reg={currentRegistration} noteObject={noteObjectId} monitor={noteObject.MonitorId} " +
                $"previousNoteIndex={previousNoteIndex} " +
                $"needGuide={needGuide} assignedThisCycle={assignedThisCycle} enableSeen={enableSeen} guide={guideId} " +
                $"{DescribeNote(note)} {DescribeGuide(guide)}");
        }

        public static void OnNoteInitializeAfter(
            NoteBase noteObject,
            NoteData note,
            bool needGuide,
            NoteGuide guide,
            int soflanGroup,
            bool isInSoflan)
        {
            if (!needGuide && !IsGuideNote(note))
                return;

            var noteObjectId = GetUnityObjectId(noteObject);
            var guideId = GetUnityObjectId(guide);
            notesAwaitingInitialize.Remove(noteObjectId);
            ClearReportedNoteAnomalies(noteObjectId);
            activeNotes[noteObjectId] = new ActiveNoteState(
                noteObject,
                note,
                guide,
                note.indexNote,
                guideId,
                GetGuidePoolId(guide),
                needGuide);
            Write("NOTE-INIT-AFTER",
                $"reg={currentRegistration} noteObject={noteObjectId} monitor={noteObject.MonitorId} " +
                $"needGuide={needGuide} guide={guideId} " +
                $"soflan={isInSoflan} group={soflanGroup} noteActive={IsActive(noteObject)} " +
                $"{DescribeNote(note)} {DescribeGuide(guide)} {DescribeCurrentPoolCounts()}");
        }

        public static void OnNoteInitializeFailed(
            NoteBase noteObject,
            NoteData note,
            bool needGuide,
            NoteGuide guide,
            bool assignedThisCycle,
            Exception exception)
        {
            Write("NOTE-INIT-EXCEPTION",
                $"reg={currentRegistration} noteObject={GetUnityObjectId(noteObject)} needGuide={needGuide} " +
                $"assignedThisCycle={assignedThisCycle} guide={GetUnityObjectId(guide)} {DescribeNote(note)} " +
                $"exception={exception.GetType().FullName}: {exception.Message} {DescribeGuide(guide)} {DescribeCounts()}");
        }

        public static void OnNoteEndBefore(
            NoteBase noteObject,
            int noteIndex,
            bool needGuide,
            NoteGuide guide,
            object noteStatus)
        {
            if (!needGuide && guide == null)
                return;

            Write("NOTE-END-BEFORE",
                $"noteObject={GetUnityObjectId(noteObject)} note={noteIndex} needGuide={needGuide} " +
                $"status={noteStatus} noteActive={IsActive(noteObject)} {DescribeGuide(guide)}");
        }

        public static void OnNoteEndAfter(NoteBase noteObject, int noteIndex, NoteGuide guide)
        {
            var noteObjectId = GetUnityObjectId(noteObject);
            var guideId = GetUnityObjectId(guide);
            activeNotes.Remove(noteObjectId);
            RemoveGuideOwner(guideId, noteObjectId);
            notesAwaitingInitialize.Remove(noteObjectId);
            ClearReportedNoteAnomalies(noteObjectId);
            Write("NOTE-END-AFTER",
                $"noteObject={noteObjectId} note={noteIndex} noteActive={IsActive(noteObject)} " +
                $"{DescribeGuide(guide)} {DescribeCounts()}");
        }

        public static void OnNoteEndFailed(
            NoteBase noteObject,
            int noteIndex,
            bool needGuide,
            NoteGuide guide,
            Exception exception)
        {
            Write("NOTE-END-EXCEPTION",
                $"noteObject={GetUnityObjectId(noteObject)} note={noteIndex} needGuide={needGuide} " +
                $"exception={exception.GetType().FullName}: {exception.Message} {DescribeGuide(guide)}");
        }

        public static void OnGuideAwake(NoteGuide guide)
        {
            var guideId = GetUnityObjectId(guide);
            if (guideId != 0)
            {
                guides[guideId] = guide;
                guidePoolIds[guideId] = GetGuidePoolId(guide);
            }

            Write("GUIDE-AWAKE",
                $"pool={GetGuidePoolId(guide)} {DescribeGuide(guide)} totalTracked={guides.Count}");
        }

        public static void OnGuideEnabled(NoteGuide guide)
        {
            var guideId = GetUnityObjectId(guide);
            if (guideId != 0)
            {
                guides[guideId] = guide;
                if (!guidePoolIds.ContainsKey(guideId))
                    guidePoolIds[guideId] = GetGuidePoolId(guide);
                guideEnableCounts.TryGetValue(guideId, out var enableCount);
                guideEnableCounts[guideId] = enableCount + 1;
                guidesAwaitingInitialize.Add(guideId);
                reportedGuideAwaitingInitialize.Remove(guideId);
            }

            Write("GUIDE-ENABLE",
                $"reg={currentRegistration} enableCount={GetCount(guideEnableCounts, guideId)} " +
                $"awaitingInitialize=True {DescribeGuide(guide)}");
        }

        public static void OnGuideDisabled(NoteGuide guide)
        {
            var guideId = GetUnityObjectId(guide);
            var initializedSinceEnable = !guidesAwaitingInitialize.Contains(guideId);
            var trackedOwner = guideOwners.TryGetValue(guideId, out var owner);
            var eventName = !initializedSinceEnable && trackedOwner
                ? "GUIDE-DISABLED-BEFORE-INIT"
                : "GUIDE-DISABLE";

            Write(eventName,
                $"reg={currentRegistration} initializedSinceEnable={initializedSinceEnable} " +
                $"trackedOwner={trackedOwner} owner={owner} {DescribeGuide(guide)}");

            guidesAwaitingInitialize.Remove(guideId);
            reportedGuideAwaitingInitialize.Remove(guideId);
        }

        public static void OnNoteEnabled(
            NoteBase noteObject,
            int noteIndex,
            bool needGuide,
            NoteGuide guide,
            bool endFlag,
            object noteStatus)
        {
            var noteObjectId = GetUnityObjectId(noteObject);
            var tracked = activeNotes.ContainsKey(noteObjectId);
            if (!needGuide && guide == null && !tracked)
                return;

            noteEnableCounts.TryGetValue(noteObjectId, out var enableCount);
            noteEnableCounts[noteObjectId] = enableCount + 1;
            notesAwaitingInitialize.Add(noteObjectId);
            ClearReportedNoteAnomalies(noteObjectId);

            Write("NOTE-ENABLE",
                $"reg={currentRegistration} enableCount={enableCount + 1} tracked={tracked} " +
                $"noteObject={noteObjectId} monitor={noteObject.MonitorId} previousNote={noteIndex} " +
                $"needGuide={needGuide} endFlag={endFlag} status={noteStatus} {DescribeGuide(guide)}");
        }

        public static void OnNoteDisabled(
            NoteBase noteObject,
            int noteIndex,
            bool needGuide,
            NoteGuide guide,
            bool endFlag,
            object noteStatus)
        {
            var noteObjectId = GetUnityObjectId(noteObject);
            var tracked = activeNotes.ContainsKey(noteObjectId);
            if (!needGuide && guide == null && !tracked)
                return;

            var eventName = tracked && !endFlag ? "NOTE-DISABLED-WITHOUT-END" : "NOTE-DISABLE";
            Write(eventName,
                $"reg={currentRegistration} tracked={tracked} awaitingInitialize={notesAwaitingInitialize.Contains(noteObjectId)} " +
                $"noteObject={noteObjectId} monitor={noteObject.MonitorId} note={noteIndex} " +
                $"needGuide={needGuide} endFlag={endFlag} status={noteStatus} {DescribeGuide(guide)}");

            notesAwaitingInitialize.Remove(noteObjectId);
            if (eventName == "NOTE-DISABLED-WITHOUT-END")
                reportedNoteDisabledWithoutEnd.Add(noteObjectId);
        }

        public static void OnGuideInitializeBefore(NoteGuide guide, int angle, int eachIndex)
        {
            var guideId = GetUnityObjectId(guide);
            if (!guidesAwaitingInitialize.Contains(guideId))
            {
                Write("GUIDE-INIT-WITHOUT-ENABLE",
                    $"reg={currentRegistration} angle={angle} requestedEach={eachIndex} {DescribeGuide(guide)}");
            }
            Write("GUIDE-INIT-BEFORE",
                $"reg={currentRegistration} angle={angle} requestedEach={eachIndex} {DescribeGuide(guide)}");
        }

        public static void OnGuideInitializeAfter(NoteGuide guide, int angle, int eachIndex)
        {
            var guideId = GetUnityObjectId(guide);
            guidesAwaitingInitialize.Remove(guideId);
            reportedGuideAwaitingInitialize.Remove(guideId);
            Write("GUIDE-INIT-AFTER",
                $"reg={currentRegistration} angle={angle} requestedEach={eachIndex} {DescribeGuide(guide)}");
        }

        public static void OnGuideInitializeFailed(NoteGuide guide, int angle, int eachIndex, Exception exception)
        {
            Write("GUIDE-INIT-EXCEPTION",
                $"reg={currentRegistration} angle={angle} requestedEach={eachIndex} " +
                $"exception={exception.GetType().FullName}: {exception.Message} {DescribeGuide(guide)}");
        }

        public static void OnGuideHideEachBefore(NoteGuide guide)
        {
            Write("EACH-HIDE-BEFORE", DescribeGuide(guide));
        }

        public static void OnGuideHideEachAfter(NoteGuide guide)
        {
            Write("EACH-HIDE-AFTER", DescribeGuide(guide));
        }

        public static void OnGuideReturnBefore(NoteGuide guide)
        {
            Write("GUIDE-RETURN-BEFORE", DescribeGuide(guide));
        }

        public static void OnGuideReturnAfter(NoteGuide guide)
        {
            var guideId = GetUnityObjectId(guide);
            guideOwners.Remove(guideId);
            guidesAwaitingInitialize.Remove(guideId);
            reportedGuideAwaitingInitialize.Remove(guideId);
            var residual = IsEachChildActive(guide) ? " residualEach=True" : string.Empty;
            Write("GUIDE-RETURN-AFTER", DescribeGuide(guide) + residual);
        }

        private static string DescribeEachDumpGuide(int dumpId, int itemIndex, NoteGuide guide, float currentMsec)
        {
            try
            {
                var guideId = GetUnityObjectId(guide);
                guideOwners.TryGetValue(guideId, out var ownerNoteObjectId);
                var hierarchyNote = guide.GetComponentInParent<NoteBase>();
                var hierarchyNoteObjectId = GetUnityObjectId(hierarchyNote);

                var hasState = false;
                var stateSource = "missing";
                var state = default(ActiveNoteState);
                if (ownerNoteObjectId != 0 && activeNotes.TryGetValue(ownerNoteObjectId, out state))
                {
                    hasState = true;
                    stateSource = "owner";
                }
                else if (hierarchyNoteObjectId != 0 && activeNotes.TryGetValue(hierarchyNoteObjectId, out state))
                {
                    hasState = true;
                    stateSource = "hierarchy";
                }

                GetEachActivity(guide, out var activeSelfCount, out var activeInHierarchyCount);
                var text = new StringBuilder(1024);
                text.Append($"dump={dumpId} item={itemIndex} currentMsec={currentMsec:F3} ");
                text.Append($"pool={GetGuidePoolId(guide)} ownerNoteObject={ownerNoteObjectId} ");
                text.Append($"hierarchyNoteObject={hierarchyNoteObjectId} stateSource={stateSource} ");
                text.Append($"ownerMatchesHierarchy={ownerNoteObjectId != 0 && ownerNoteObjectId == hierarchyNoteObjectId} ");
                text.Append($"eachChildActiveSelf={activeSelfCount} eachChildActiveInHierarchy={activeInHierarchyCount} ");
                text.Append(DescribeTransform("guideRoot", guide.transform));

                var rootRenderer = guide.GetComponent<SpriteRenderer>();
                text.Append(' ');
                text.Append(DescribeSpriteRenderer("guideRenderer", rootRenderer));
                text.Append(' ');
                text.Append(DescribeNoteObject("hierarchyNote", hierarchyNote));

                if (hasState)
                {
                    text.Append($" stateGuideMatches={state.GuideObject == guide} ");
                    text.Append(DescribeNoteObject("trackedNote", state.NoteObject));
                    text.Append(' ');
                    text.Append(DescribeDumpNote(state, currentMsec));
                }
                else
                {
                    text.Append(" trackedNote=<missing> noteData=<missing>");
                }

                return text.ToString();
            }
            catch (Exception exception)
            {
                return $"dump={dumpId} item={itemIndex} guide={GetUnityObjectId(guide)} " +
                       $"describeError={exception.GetType().FullName}:{exception.Message}";
            }
        }

        private static void WriteEachChildren(int dumpId, int itemIndex, NoteGuide guide)
        {
            try
            {
                var guideId = GetUnityObjectId(guide);
                var root = guide.transform;
                for (var childIndex = 0; childIndex < root.childCount; childIndex++)
                {
                    var child = root.GetChild(childIndex);
                    var renderers = child.GetComponentsInChildren<SpriteRenderer>(true);
                    Write("EACH-DUMP-CHILD",
                        $"dump={dumpId} item={itemIndex} guide={guideId} childIndex={childIndex} " +
                        $"rendererCount={renderers.Length} {DescribeTransform("eachChild", child)}");

                    for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                    {
                        var renderer = renderers[rendererIndex];
                        Write("EACH-DUMP-RENDERER",
                            $"dump={dumpId} item={itemIndex} guide={guideId} childIndex={childIndex} " +
                            $"rendererIndex={rendererIndex} {DescribeTransform("rendererTransform", renderer.transform)} " +
                            DescribeSpriteRenderer("renderer", renderer));
                    }
                }
            }
            catch (Exception exception)
            {
                Write("EACH-DUMP-CHILD-ERROR",
                    $"dump={dumpId} item={itemIndex} guide={GetUnityObjectId(guide)} " +
                    $"exception={exception.GetType().FullName}:{exception.Message}");
            }
        }

        private static string DescribeDumpNote(ActiveNoteState state, float currentMsec)
        {
            var note = state.NoteData;
            if (note == null)
                return $"noteData=<null> cachedNoteIndex={state.NoteIndex}";

            try
            {
                var manager = Singleton<SoflanManager>.Instance;
                var group = manager != null ? manager.getNoteSoflanGroup(note) : 0;
                var audioMsec = manager != null
                    ? manager.getNoteAudioMsecForSoflan(note.indexNote, note.time.msec)
                    : note.time.msec;
                var endMsec = manager != null
                    ? manager.getNoteEndAudioMsecForSoflan(note.indexNote, note.end.msec)
                    : note.end.msec;
                var noteSoflanTime = manager != null
                    ? manager.ConvertAudioTimeToY_PreviewMode(audioMsec, group)
                    : audioMsec;
                var currentSoflanTime = manager != null
                    ? manager.ConvertAudioTimeToY_PreviewMode(currentMsec, group)
                    : currentMsec;

                return $"noteDataIndex={note.indexNote} kind={note.type.getEnum()} button={note.startButtonPos} " +
                       $"audioMsec={audioMsec:F3} rawAudioMsec={note.time.msec:F3} endMsec={endMsec:F3} " +
                       $"rawEndMsec={note.end.msec:F3} timeGrid={note.time.grid} endGrid={note.end.grid} " +
                       $"audioDelta={audioMsec - currentMsec:F3} group={group} noteSoflanTime={noteSoflanTime:F3} " +
                       $"currentSoflanTime={currentSoflanTime:F3} soflanDelta={noteSoflanTime - currentSoflanTime:F3} " +
                       $"isEach={note.isEach} eachIndex={note.indexEach} eachChildren={note.eachChild?.Count ?? -1} " +
                       $"used={note.isUsed} judged={note.isJudged}";
            }
            catch (Exception exception)
            {
                return $"noteDataIndex={note.indexNote} describeError={exception.GetType().Name}:{exception.Message}";
            }
        }

        private static string DescribeNoteObject(string label, NoteBase noteObject)
        {
            if (noteObject == null)
                return $"{label}=<null>";

            try
            {
                return $"{label}={GetUnityObjectId(noteObject)} type={noteObject.GetType().FullName} " +
                       $"monitor={noteObject.MonitorId} {DescribeTransform(label + "Transform", noteObject.transform)}";
            }
            catch (Exception exception)
            {
                return $"{label}={GetUnityObjectId(noteObject)} describeError={exception.GetType().Name}:{exception.Message}";
            }
        }

        private static string DescribeTransform(string label, Transform transform)
        {
            if (transform == null)
                return $"{label}=<null>";

            try
            {
                var position = transform.position;
                var localPosition = transform.localPosition;
                var scale = transform.localScale;
                var lossyScale = transform.lossyScale;
                var euler = transform.eulerAngles;
                var localEuler = transform.localEulerAngles;
                var rotation = transform.rotation;
                var localRotation = transform.localRotation;
                var gameObject = transform.gameObject;
                return $"{label}={GetUnityObjectId(transform)} name={gameObject.name} " +
                       $"activeSelf={gameObject.activeSelf} activeInHierarchy={gameObject.activeInHierarchy} " +
                       $"layer={gameObject.layer} childCount={transform.childCount} " +
                       $"worldPos=({position.x:F3},{position.y:F3},{position.z:F3}) " +
                       $"localPos=({localPosition.x:F3},{localPosition.y:F3},{localPosition.z:F3}) " +
                       $"localScale=({scale.x:F3},{scale.y:F3},{scale.z:F3}) " +
                       $"lossyScale=({lossyScale.x:F3},{lossyScale.y:F3},{lossyScale.z:F3}) " +
                       $"worldEuler=({euler.x:F3},{euler.y:F3},{euler.z:F3}) " +
                       $"localEuler=({localEuler.x:F3},{localEuler.y:F3},{localEuler.z:F3}) " +
                       $"worldRotation=({rotation.x:F4},{rotation.y:F4},{rotation.z:F4},{rotation.w:F4}) " +
                       $"localRotation=({localRotation.x:F4},{localRotation.y:F4},{localRotation.z:F4},{localRotation.w:F4})";
            }
            catch (Exception exception)
            {
                return $"{label}={GetUnityObjectId(transform)} describeError={exception.GetType().Name}:{exception.Message}";
            }
        }

        private static string DescribeSpriteRenderer(string label, SpriteRenderer renderer)
        {
            if (renderer == null)
                return $"{label}=<null>";

            try
            {
                var color = renderer.color;
                var sprite = renderer.sprite;
                return $"{label}={GetUnityObjectId(renderer)} enabled={renderer.enabled} visible={renderer.isVisible} " +
                       $"sortingLayer={renderer.sortingLayerName} sortingOrder={renderer.sortingOrder} " +
                       $"sprite={(sprite != null ? sprite.name : "<null>")} " +
                       $"color=({color.r:F3},{color.g:F3},{color.b:F3},{color.a:F3})";
            }
            catch (Exception exception)
            {
                return $"{label}={GetUnityObjectId(renderer)} describeError={exception.GetType().Name}:{exception.Message}";
            }
        }

        private static void GetEachActivity(NoteGuide guide, out int activeSelfCount, out int activeInHierarchyCount)
        {
            activeSelfCount = 0;
            activeInHierarchyCount = 0;
            try
            {
                if (guide == null)
                    return;

                var root = guide.transform;
                for (var i = 0; i < root.childCount; i++)
                {
                    var childObject = root.GetChild(i).gameObject;
                    if (childObject.activeSelf)
                        activeSelfCount++;
                    if (childObject.activeInHierarchy)
                        activeInHierarchyCount++;
                }
            }
            catch
            {
                activeSelfCount = 0;
                activeInHierarchyCount = 0;
            }
        }

        private static void WriteSnapshot(string eventName)
        {
            Write(eventName, $"msec={GetCurrentMsec():F3} {DescribeCounts()}");

            var pools = new HashSet<int>();
            foreach (var pair in guidePoolIds)
            {
                if (pair.Value != 0)
                    pools.Add(pair.Value);
            }
            foreach (var poolId in pools)
                Write("POOL-SNAPSHOT", $"pool={poolId} {DescribeCounts(poolId)}");

            foreach (var pair in guides)
            {
                var guide = pair.Value;
                if (guide == null)
                    continue;
                if (!guide.gameObject.activeSelf && IsEachChildActive(guide))
                {
                    if (reportedInactiveEach.Add(pair.Key))
                        Write("INACTIVE-GUIDE-HAS-EACH", DescribeGuide(guide));
                }
                else
                {
                    reportedInactiveEach.Remove(pair.Key);
                }

                if (guide.gameObject.activeSelf && guidesAwaitingInitialize.Contains(pair.Key))
                {
                    if (reportedGuideAwaitingInitialize.Add(pair.Key))
                        Write("ACTIVE-GUIDE-WAITING-INIT", DescribeGuide(guide));
                }
                else
                {
                    reportedGuideAwaitingInitialize.Remove(pair.Key);
                }
            }

            CheckNoteGuideBindings();
        }

        private static void CheckNoteGuideBindings()
        {
            foreach (var pair in activeNotes)
            {
                var noteObjectId = pair.Key;
                var state = pair.Value;
                var noteObject = state.NoteObject;
                var guide = state.GuideObject;
                var noteActive = IsActive(noteObject);
                var guideActive = guide != null && guide.gameObject.activeSelf;
                var guideAttached = noteObject != null && guide != null && guide.transform.parent == noteObject.transform;

                if (!noteActive)
                {
                    if (reportedNoteDisabledWithoutEnd.Add(noteObjectId))
                    {
                        Write("TRACKED-NOTE-INACTIVE",
                            $"{DescribeActiveNote(noteObjectId)} noteParent={GetParentDescription(noteObject)} " +
                            $"{DescribeGuide(guide)}");
                    }
                }
                else
                {
                    reportedNoteDisabledWithoutEnd.Remove(noteObjectId);
                }

                if (noteActive && state.NeedGuide && !guideActive)
                {
                    if (reportedActiveNoteInactiveGuide.Add(noteObjectId))
                    {
                        Write("ACTIVE-NOTE-INACTIVE-GUIDE",
                            $"{DescribeActiveNote(noteObjectId)} noteParent={GetParentDescription(noteObject)} " +
                            $"{DescribeGuide(guide)}");
                    }
                }
                else
                {
                    reportedActiveNoteInactiveGuide.Remove(noteObjectId);
                }

                if (noteActive && guideActive && !guideAttached)
                {
                    if (reportedActiveNoteDetachedGuide.Add(noteObjectId))
                    {
                        Write("ACTIVE-NOTE-GUIDE-DETACHED",
                            $"{DescribeActiveNote(noteObjectId)} noteParent={GetParentDescription(noteObject)} " +
                            $"{DescribeGuide(guide)}");
                    }
                }
                else
                {
                    reportedActiveNoteDetachedGuide.Remove(noteObjectId);
                }

                if (!noteActive && guideActive)
                {
                    if (reportedInactiveNoteActiveGuide.Add(noteObjectId))
                    {
                        Write("INACTIVE-NOTE-ACTIVE-GUIDE",
                            $"{DescribeActiveNote(noteObjectId)} noteParent={GetParentDescription(noteObject)} " +
                            $"{DescribeGuide(guide)}");
                    }
                }
                else
                {
                    reportedInactiveNoteActiveGuide.Remove(noteObjectId);
                }

                var owner = 0;
                var ownerMatches = state.GuideId != 0
                    && guideOwners.TryGetValue(state.GuideId, out owner)
                    && owner == noteObjectId;
                if (noteActive && guideActive && !ownerMatches)
                {
                    if (reportedGuideOwnerMismatch.Add(noteObjectId))
                    {
                        Write("ACTIVE-NOTE-GUIDE-OWNER-MISMATCH",
                            $"expectedOwner={noteObjectId} actualOwner={owner} {DescribeActiveNote(noteObjectId)} " +
                            $"{DescribeGuide(guide)}");
                    }
                }
                else
                {
                    reportedGuideOwnerMismatch.Remove(noteObjectId);
                }
            }
        }

        private static string DescribeCounts(int poolId = 0)
        {
            var total = 0;
            var active = 0;
            var activeEach = 0;
            var inactiveEach = 0;
            var owners = 0;
            var poolActiveNotes = 0;
            foreach (var pair in guides)
            {
                var guide = pair.Value;
                if (guide == null)
                    continue;
                if (poolId != 0 && GetGuidePoolId(guide) != poolId)
                    continue;

                total++;
                if (guideOwners.ContainsKey(pair.Key))
                    owners++;
                var guideActive = guide.gameObject.activeSelf;
                var eachActive = IsEachChildActive(guide);
                if (guideActive)
                {
                    active++;
                    if (eachActive)
                        activeEach++;
                }
                else if (eachActive)
                {
                    inactiveEach++;
                }
            }

            foreach (var pair in activeNotes)
            {
                if (poolId == 0 || pair.Value.PoolId == poolId)
                    poolActiveNotes++;
            }

            return $"guides(total={total},active={active},free={total - active},activeEach={activeEach}," +
                   $"inactiveEach={inactiveEach},owners={owners}) activeNotes={poolActiveNotes}";
        }

        private static string DescribeCurrentPoolCounts()
        {
            var poolId = GetCurrentPoolId();
            return poolId != 0 ? DescribeCounts(poolId) : DescribeCounts();
        }

        private static string DescribeActiveNote(int noteObjectId)
        {
            return activeNotes.TryGetValue(noteObjectId, out var state)
                ? $"noteObject={noteObjectId},note={state.NoteIndex},guide={state.GuideId}," +
                  $"pool={state.PoolId},needGuide={state.NeedGuide}"
                : $"noteObject={noteObjectId},state=<missing>";
        }

        private static string GetParentDescription(NoteBase noteObject)
        {
            try
            {
                var parent = noteObject != null ? noteObject.transform.parent : null;
                return parent != null ? $"{parent.name}({GetUnityObjectId(parent)})" : "<null>";
            }
            catch
            {
                return "<error>";
            }
        }

        private static string DescribeNote(NoteData note)
        {
            if (note == null)
                return "note=<null>";

            var group = 0;
            try { group = Singleton<SoflanManager>.Instance.getNoteSoflanGroup(note); }
            catch { }

            return $"note={note.indexNote} kind={note.type.getEnum()} button={note.startButtonPos} " +
                   $"audio={note.time.msec:F3} end={note.end.msec:F3} each={note.isEach} " +
                   $"eachIndex={note.indexEach} eachChildren={note.eachChild?.Count ?? -1} " +
                   $"used={note.isUsed} judged={note.isJudged} group={group} msec={GetCurrentMsec():F3}";
        }

        private static string DescribeGuide(NoteGuide guide)
        {
            if (guide == null)
                return "guide=<null>";

            try
            {
                var guideId = guide.GetInstanceID();
                var parent = guide.transform.parent;
                var rootRenderer = guide.GetComponent<SpriteRenderer>();
                var alpha = rootRenderer != null ? rootRenderer.color.a : -1f;
                var scale = guide.transform.localScale;
                var childActive = IsEachChildActive(guide);
                guideOwners.TryGetValue(guideId, out var owner);
                return $"guide={guideId} active={guide.gameObject.activeSelf} eachIndex={guide.EachIndex} " +
                       $"eachActive={childActive} alpha={alpha:F3} scale=({scale.x:F3},{scale.y:F3},{scale.z:F3}) " +
                       $"parent={(parent != null ? parent.name : "<null>")} parentId={GetUnityObjectId(parent)} owner={owner}";
            }
            catch (Exception exception)
            {
                return $"guide={GetUnityObjectId(guide)} describeError={exception.GetType().Name}:{exception.Message}";
            }
        }

        private static bool IsGuideNote(NoteData note)
        {
            if (note == null)
                return false;

            switch (note.type.getEnum())
            {
                case NotesTypeID.Def.Begin:
                case NotesTypeID.Def.ExTap:
                case NotesTypeID.Def.Hold:
                case NotesTypeID.Def.ExHold:
                case NotesTypeID.Def.BreakHold:
                case NotesTypeID.Def.ExBreakHold:
                case NotesTypeID.Def.Star:
                case NotesTypeID.Def.ExStar:
                case NotesTypeID.Def.BreakStar:
                case NotesTypeID.Def.ExBreakStar:
                case NotesTypeID.Def.Break:
                case NotesTypeID.Def.ExBreakTap:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsEachChildActive(NoteGuide guide)
        {
            try
            {
                return guide != null
                    && guide.transform.childCount > 0
                    && guide.transform.GetChild(0).gameObject.activeSelf;
            }
            catch
            {
                return false;
            }
        }

        private static int GetGuidePoolId(NoteGuide guide)
        {
            if (guide == null)
                return 0;

            var guideId = GetUnityObjectId(guide);
            try
            {
                var poolTransform = guide.ParentTransform != null ? guide.ParentTransform : guide.transform.parent;
                var poolId = GetUnityObjectId(poolTransform);
                if (poolId != 0 && guideId != 0)
                    guidePoolIds[guideId] = poolId;
                return poolId;
            }
            catch
            {
                return guidePoolIds.TryGetValue(guideId, out var poolId) ? poolId : 0;
            }
        }

        private static int GetCurrentPoolId()
        {
            return GetControllerPoolId(currentControllerId);
        }

        private static int GetControllerPoolId(int controllerId)
        {
            return controllerId != 0 && controllerPoolIds.TryGetValue(controllerId, out var poolId) ? poolId : 0;
        }

        private static bool IsActive(NoteBase noteObject)
        {
            try { return noteObject != null && noteObject.gameObject.activeSelf; }
            catch { return false; }
        }

        private static int GetUnityObjectId(object value)
        {
            try
            {
                var unityObject = value as UnityEngine.Object;
                return unityObject != null ? unityObject.GetInstanceID() : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static float GetCurrentMsec()
        {
            try { return NotesManager.GetCurrentMsec(); }
            catch { return float.NaN; }
        }

        private static void RemoveGuideOwner(int guideId, int noteObjectId)
        {
            if (guideId != 0 && guideOwners.TryGetValue(guideId, out var owner) && owner == noteObjectId)
                guideOwners.Remove(guideId);
        }

        private static void ClearCurrentRegistration()
        {
            currentRegistration = 0;
            currentControllerId = 0;
            currentRegistrationTracksGuide = false;
            currentRegistrationAssignedGuide = false;
            currentRegistrationInitializedNote = false;
        }

        private static int GetCount(Dictionary<int, int> counts, int key)
        {
            return key != 0 && counts.TryGetValue(key, out var count) ? count : 0;
        }

        private static void ClearReportedNoteAnomalies(int noteObjectId)
        {
            reportedActiveNoteInactiveGuide.Remove(noteObjectId);
            reportedActiveNoteDetachedGuide.Remove(noteObjectId);
            reportedInactiveNoteActiveGuide.Remove(noteObjectId);
            reportedNoteDisabledWithoutEnd.Remove(noteObjectId);
            reportedGuideOwnerMismatch.Remove(noteObjectId);
        }

        private static void ClearReportedNoteAnomalies()
        {
            reportedActiveNoteInactiveGuide.Clear();
            reportedActiveNoteDetachedGuide.Clear();
            reportedInactiveNoteActiveGuide.Clear();
            reportedNoteDisabledWithoutEnd.Clear();
            reportedGuideOwnerMismatch.Clear();
        }

        private static void Write(string eventName, string message)
        {
            PatchLog.WriteLine($"[GuideDiag][{++eventSequence:D8}][{eventName}] {message}");
        }

        private readonly struct ActiveNoteState
        {
            public readonly int NoteIndex;
            public readonly int GuideId;
            public readonly int PoolId;
            public readonly bool NeedGuide;
            public readonly NoteBase NoteObject;
            public readonly NoteData NoteData;
            public readonly NoteGuide GuideObject;

            public ActiveNoteState(
                NoteBase noteObject,
                NoteData noteData,
                NoteGuide guideObject,
                int noteIndex,
                int guideId,
                int poolId,
                bool needGuide)
            {
                NoteObject = noteObject;
                NoteData = noteData;
                GuideObject = guideObject;
                NoteIndex = noteIndex;
                GuideId = guideId;
                PoolId = poolId;
                NeedGuide = needGuide;
            }
        }
    }
}
#endif
