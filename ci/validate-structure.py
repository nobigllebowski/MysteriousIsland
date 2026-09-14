#!/usr/bin/env python3
"""Structural validator for the Vardholm C# sources.

There is no C# compiler and no Unity in the environment this project is written
in, so nothing catches the two failure modes that actually kill a codebase
authored this way: a file that does not parse, and a file that names a type
which was never written. This script is the stand-in. It is a lexer plus a set
of structural rules, not a type checker -- it cannot prove the project compiles,
but every error it reports is something that definitely would not.

Python 3.8+, standard library only. Usage:

    python3 ci/validate-structure.py [PROJECT_ROOT] [options]

Exit codes:
    0  no errors (warnings may have been printed)
    1  at least one error
    2  the script could not run (bad root, unreadable file)
"""

import argparse
import json
import os
import re
import sys

# ---------------------------------------------------------------------------
# Project layout
# ---------------------------------------------------------------------------

SCRIPTS_DIR = os.path.join("Assets", "Scripts")
TESTS_DIR = os.path.join("Assets", "Tests")
CORE_DIR = os.path.join(SCRIPTS_DIR, "Core")
GAME_DIR = os.path.join(SCRIPTS_DIR, "Game")
UI_DIR = os.path.join(SCRIPTS_DIR, "UI")
LOCALIZATION_CSV = os.path.join("Assets", "Localization", "en.csv")
LOCALIZATION_MIRROR_CSV = os.path.join("Assets", "Resources", "Localization", "en.csv")

# (directory, required namespace prefix). First match wins, so list the deepest
# directories first if they ever nest.
NAMESPACE_RULES = [
    (CORE_DIR, "ForgottenIsle.Core"),
    (GAME_DIR, "ForgottenIsle.Game"),
    (UI_DIR, "ForgottenIsle.UI"),
    (TESTS_DIR, "ForgottenIsle.Tests"),
]

# Namespaces a file is allowed to declare regardless of where it lives. The one
# entry is the C# 9 `init` polyfill (ADR-0003): the compiler recognises
# IsExternalInit ONLY in System.Runtime.CompilerServices, so putting it in a
# ForgottenIsle namespace would silently disable every init-only setter.
NAMESPACE_EXEMPTIONS = {"System.Runtime.CompilerServices"}

# Assembly definitions whose contents are pinned by ADR, checked beyond "is it
# valid JSON with the right name".
CORE_ASMDEF_NAME = "ForgottenIsle.Core"

# A LocKey that follows the house style: lowercase, dot separated, at least two
# segments. Anything else is reported as a shape warning rather than silently
# skipped, because an unchecked key is worse than an ugly one.
CANONICAL_LOCKEY = re.compile(r"^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$")

# A literal that could be half of a composed key (a prefix or a suffix). Used
# only to suppress "unused key" warnings -- see check F.
KEY_FRAGMENT_MIN_LENGTH = 4


# ---------------------------------------------------------------------------
# Known-good type names
# ---------------------------------------------------------------------------
#
# Check E reports any type that is referenced but never declared in this
# project. Everything the project legitimately gets from outside -- the BCL,
# UnityEngine, UI Toolkit, the Input System, NUnit -- has to be listed here or
# it reads as an invented type.
#
# ADDING A NAME: only add something you have confirmed exists in a referenced
# assembly. Adding a name to silence a report is how this check stops working;
# if a report surprises you, the type probably really is missing.

BCL_TYPES = {
    # Primitives and core value types
    "Object", "String", "Boolean", "Byte", "SByte", "Char", "Decimal", "Double",
    "Single", "Int16", "Int32", "Int64", "UInt16", "UInt32", "UInt64", "IntPtr",
    "UIntPtr", "Guid", "DateTime", "DateTimeOffset", "TimeSpan", "TimeZoneInfo",
    "Version", "Uri", "Nullable", "ValueType", "Enum", "Array", "Type", "Math",
    "MathF", "Convert", "BitConverter", "Buffer", "Environment", "Console", "GC",
    "Random", "Tuple", "ValueTuple", "Lazy", "WeakReference", "Delegate",
    "MulticastDelegate", "Attribute", "AppDomain", "Activator",
    # Interfaces and delegates
    "IDisposable", "IEquatable", "IComparable", "IComparer", "IEqualityComparer",
    "IEnumerable", "IEnumerator", "IReadOnlyList", "IReadOnlyCollection",
    "IReadOnlyDictionary", "IList", "ICollection", "IDictionary", "ISet",
    "IFormatProvider", "IFormattable", "IConvertible", "ICloneable",
    "IAsyncResult", "IProgress", "IObservable", "IObserver",
    "Action", "Func", "Predicate", "Comparison", "EventHandler", "EventArgs",
    # Collections
    "List", "Dictionary", "SortedDictionary", "SortedList", "SortedSet",
    "HashSet", "Queue", "Stack", "LinkedList", "LinkedListNode", "KeyValuePair",
    "Comparer", "EqualityComparer", "ReadOnlyCollection", "ArraySegment",
    "ConcurrentDictionary", "ConcurrentQueue", "ConcurrentBag", "BitArray",
    # Text
    "StringBuilder", "StringComparer", "StringComparison", "StringSplitOptions",
    "Encoding", "UTF8Encoding", "Rune", "CompareOptions", "NormalizationForm",
    "Regex", "Match", "MatchCollection", "Group", "Capture", "RegexOptions",
    # Globalization / formatting
    "CultureInfo", "NumberStyles", "NumberFormatInfo", "DateTimeStyles",
    "DateTimeFormatInfo", "CharUnicodeInfo", "TextInfo",
    # IO
    "Stream", "MemoryStream", "FileStream", "TextReader", "TextWriter",
    "StreamReader", "StreamWriter", "StringReader", "StringWriter", "BinaryReader",
    "BinaryWriter", "File", "FileInfo", "Directory", "DirectoryInfo", "Path",
    "FileMode", "FileAccess", "FileShare", "FileOptions", "FileAttributes",
    "SearchOption", "DriveInfo",
    # Threading / async
    "Task", "ValueTask", "TaskCompletionSource", "CancellationToken",
    "CancellationTokenSource", "Thread", "ThreadPool", "Monitor", "Interlocked",
    "ManualResetEvent", "AutoResetEvent", "SemaphoreSlim", "Volatile", "Timer",
    "SynchronizationContext",
    # Exceptions
    "Exception", "SystemException", "ApplicationException", "ArgumentException",
    "ArgumentNullException", "ArgumentOutOfRangeException", "InvalidOperationException",
    "NotSupportedException", "NotImplementedException", "NullReferenceException",
    "IndexOutOfRangeException", "KeyNotFoundException", "FormatException",
    "OverflowException", "DivideByZeroException", "ArithmeticException",
    "InvalidCastException", "ObjectDisposedException", "TimeoutException",
    "AggregateException", "IOException", "FileNotFoundException",
    "DirectoryNotFoundException", "PathTooLongException", "EndOfStreamException",
    "UnauthorizedAccessException", "OutOfMemoryException", "StackOverflowException",
    "InvalidDataException", "OperationCanceledException", "TaskCanceledException",
    # Diagnostics and attributes
    "Debug", "Trace", "Stopwatch", "Process", "StackTrace", "StackFrame",
    "Conditional", "ConditionalAttribute", "DebuggerDisplay", "DebuggerStepThrough",
    "Serializable", "SerializableAttribute", "Flags", "FlagsAttribute", "Obsolete",
    "ObsoleteAttribute", "MethodImpl", "MethodImplAttribute", "MethodImplOptions",
    "CallerMemberName", "CallerFilePath", "CallerLineNumber", "CompilerGenerated",
    "StructLayout", "LayoutKind", "FieldOffset", "MarshalAs", "UnmanagedType",
    "DllImport", "AttributeUsage", "AttributeTargets", "ThreadStatic",
    "NonSerialized", "InternalsVisibleTo", "ModuleInitializer",
    "SuppressMessage", "ExcludeFromCodeCoverage",
    # Reflection
    "Assembly", "MethodInfo", "FieldInfo", "PropertyInfo", "ConstructorInfo",
    "MemberInfo", "ParameterInfo", "BindingFlags", "TypeInfo",
    # Misc
    "Span", "ReadOnlySpan", "Memory", "ReadOnlyMemory", "IsExternalInit",
    "HashCode", "Index", "Range", "Comparer", "Console",
}

UNITY_TYPES = {
    # Core engine
    "MonoBehaviour", "ScriptableObject", "GameObject", "Transform", "RectTransform",
    "Component", "Behaviour", "Application", "Screen", "Time", "Debug", "Mathf",
    "Random", "Resources", "PlayerPrefs", "JsonUtility", "SystemInfo", "Cursor",
    "Coroutine", "AsyncOperation", "AsyncOperationHandle", "YieldInstruction",
    "CustomYieldInstruction", "WaitForSeconds", "WaitForSecondsRealtime",
    "WaitForEndOfFrame", "WaitForFixedUpdate", "WaitUntil", "WaitWhile",
    "HideFlags", "LogType", "ILogger", "ILogHandler", "Application",
    "RuntimePlatform", "DeviceType", "SystemLanguage", "ScreenOrientation",
    "FindObjectsInactive", "FindObjectsSortMode", "SceneUtility",
    # Math and geometry
    "Vector2", "Vector3", "Vector4", "Vector2Int", "Vector3Int", "Quaternion",
    "Matrix4x4", "Color", "Color32", "Rect", "RectInt", "RectOffset", "Bounds",
    "BoundsInt", "Plane", "Ray", "Ray2D", "AnimationCurve", "Keyframe", "Gradient",
    "GradientColorKey", "GradientAlphaKey", "Space", "LayerMask",
    # Assets and rendering
    "Texture", "Texture2D", "Sprite", "Material", "Shader", "Mesh", "Renderer",
    "MeshRenderer", "SkinnedMeshRenderer", "Camera", "Light", "RenderTexture",
    "TextAsset", "AudioClip", "AudioSource", "AudioListener", "Font",
    "CameraClearFlags", "LightType", "ShadowCastingMode", "QualitySettings",
    # Physics
    "Rigidbody", "Collider", "BoxCollider", "SphereCollider", "CapsuleCollider",
    "MeshCollider", "CharacterController", "Physics", "RaycastHit", "Collision",
    "ForceMode", "QueryTriggerInteraction",
    # Attributes
    "SerializeField", "SerializeReference", "RequireComponent", "DisallowMultipleComponent",
    "ExecuteAlways", "ExecuteInEditMode", "AddComponentMenu", "HideInInspector",
    "Tooltip", "Header", "Space", "TextArea", "Multiline", "ContextMenu",
    "RuntimeInitializeOnLoadMethod", "RuntimeInitializeLoadType", "CreateAssetMenu",
    "Preserve", "SelectionBase",
    # Scene management
    "SceneManager", "Scene", "LoadSceneMode", "LoadSceneParameters",
    "UnloadSceneOptions", "LocalPhysicsMode",
    # IMGUI -- the dev overlay draws with it, and nothing else may
    "GUI", "GUILayout", "GUIStyle", "GUIStyleState", "GUIContent", "GUISkin",
    "GUILayoutOption", "GUIUtility", "GUILayoutUtility", "Event", "EventType",
    "ImagePosition", "TextClipping", "ScaleMode",
    # UI Toolkit (UnityEngine.UIElements)
    "UIDocument", "PanelSettings", "VisualElement", "VisualTreeAsset", "StyleSheet",
    "TemplateContainer", "Label", "Button", "Toggle", "TextField", "Slider",
    "SliderInt", "ScrollView", "ListView", "DropdownField", "Foldout", "Image",
    "Box", "GroupBox", "RadioButton", "RadioButtonGroup", "ProgressBar",
    "MinMaxSlider", "IntegerField", "FloatField", "Vector3Field", "PopupWindow",
    "IStyle", "IResolvedStyle", "StyleColor", "StyleFloat", "StyleLength",
    "StyleEnum", "StyleBackground", "Length", "LengthUnit", "Position",
    "FlexDirection", "Wrap", "Align", "Justify", "DisplayStyle", "Visibility",
    "Overflow", "TextAnchor", "FontStyle", "WhiteSpace", "TextOverflow",
    "EasingMode", "PickingMode", "UsageHints", "ContentZoomer", "Background",
    "ScrollViewMode", "SelectionType", "AlternatingRowBackground",
    "Translate", "Scale", "Rotate", "Angle", "AngleUnit", "TransformOrigin",
    "TimeValue", "TimeUnit", "BackgroundPosition", "BackgroundRepeat",
    "BackgroundSize", "TextShadow", "StyleTranslate", "StyleScale",
    "StyleRotate", "StyleTransformOrigin", "StyleList", "StyleCursor",
    "EventCallback", "EventBase", "CallbackEventHandler", "IEventHandler",
    "ClickEvent", "PointerDownEvent", "PointerUpEvent", "PointerMoveEvent",
    "PointerCancelEvent", "PointerCaptureOutEvent", "PointerOverEvent",
    "PointerOutEvent", "PointerEnterEvent", "PointerLeaveEvent",
    "MouseDownEvent", "MouseUpEvent", "MouseEnterEvent", "MouseLeaveEvent",
    "KeyDownEvent", "KeyUpEvent", "GeometryChangedEvent", "AttachToPanelEvent",
    "DetachFromPanelEvent", "FocusInEvent", "FocusOutEvent", "ChangeEvent",
    "NavigationSubmitEvent", "NavigationCancelEvent", "NavigationMoveEvent",
    "TransitionEndEvent", "IPanel", "PanelScaleMode", "PanelScreenMatchMode",
    "Clickable", "Manipulator", "MouseManipulator", "IVisualElementScheduler",
    "IVisualElementScheduledItem", "TimerState", "UQueryBuilder", "UQueryState",
    "Cursor", "CursorStyle", "TextElement", "BaseField", "INotifyValueChanged",
    # Input System
    "InputSystem", "InputAction", "InputActionAsset", "InputActionMap",
    "InputActionReference", "InputControl", "InputDevice", "InputBinding",
    "InputValue", "InputActionPhase", "CallbackContext", "Keyboard", "Mouse",
    "Touchscreen", "Gamepad", "Key", "PlayerInput", "InputUser",
    "IInputActionCollection", "IInputActionCollection2", "InputControlScheme",
    "InputActionSetupExtensions", "InputActionRebindingExtensions",
    "TouchPhase", "TouchControl", "Pointer", "StickControl", "ButtonControl",
    # Addressables (referenced in the package manifest)
    "Addressables", "AssetReference", "AsyncOperationStatus",
}

TEST_TYPES = {
    # NUnit
    "Assert", "Assume", "Is", "Has", "Does", "Throws", "Contains", "Iz",
    "CollectionAssert", "StringAssert", "DirectoryAssert", "FileAssert",
    "Constraint", "IResolveConstraint", "TestContext", "TestDelegate",
    "AsyncTestDelegate", "TestFixture", "TestFixtureAttribute", "Test",
    "TestAttribute", "TestCase", "TestCaseAttribute", "TestCaseSource",
    "TestCaseSourceAttribute", "SetUp", "SetUpAttribute", "TearDown",
    "TearDownAttribute", "OneTimeSetUp", "OneTimeSetUpAttribute", "OneTimeTearDown",
    "OneTimeTearDownAttribute", "Category", "CategoryAttribute", "Description",
    "DescriptionAttribute", "Ignore", "IgnoreAttribute", "Explicit",
    "ExplicitAttribute", "Order", "OrderAttribute", "Repeat", "RepeatAttribute",
    "Retry", "RetryAttribute", "Timeout", "TimeoutAttribute", "Values",
    "ValuesAttribute", "ValueSource", "ValueSourceAttribute", "Range",
    "RangeAttribute", "Random", "RandomAttribute", "Author", "AuthorAttribute",
    "Property", "PropertyAttribute", "Apartment", "MaxTime", "Parallelizable",
    "SuccessException", "IgnoreException", "AssertionException",
    # Unity Test Framework
    "UnityTest", "UnityTestAttribute", "UnitySetUp", "UnitySetUpAttribute",
    "UnityTearDown", "UnityTearDownAttribute", "UnityPlatform",
    "UnityPlatformAttribute", "LogAssert", "MonoBehaviourTest",
    "IMonoBehaviourTest", "IPrebuildSetup", "IPostBuildCleanup",
    "PrebuildSetup", "PostBuildCleanup", "RuntimeTestLauncher", "TestRunnerApi",
    "ConditionalIgnore", "ConditionalIgnoreAttribute", "RequirePlatformSupport",
}

# UnityEditor types. Kept in their own set rather than folded into UNITY_TYPES so that the
# distinction stays visible: anything here is editor-only and must never appear outside
# ForgottenIsle.Editor or an "#if UNITY_EDITOR" block. The engine-free-Core check and the
# layering gate are what enforce that; this set only stops the contract scanner reporting
# real Unity types as undeclared.
UNITY_EDITOR_TYPES = {
    "EditorWindow", "Editor", "EditorGUI", "EditorGUILayout", "EditorGUIUtility",
    "EditorApplication", "EditorUtility", "EditorPrefs", "AssetDatabase",
    "EditorBuildSettings", "EditorBuildSettingsScene", "BuildPipeline",
    "BuildPlayerOptions", "BuildReport", "BuildTarget", "BuildTargetGroup",
    "EditorSceneManager", "NewSceneSetup", "NewSceneMode", "OpenSceneMode",
    "SceneAsset", "MenuItem", "InitializeOnLoad", "InitializeOnLoadMethod",
    "InitializeOnLoadMethodAttribute", "InitializeOnLoadAttribute",
    "MenuItemAttribute", "SerializedObject", "SerializedProperty",
    "PropertyDrawer", "CustomEditor", "CustomEditorAttribute", "Selection",
    "Undo", "PrefabUtility", "GUIContent", "GUILayout", "GUI", "GUIStyle",
    "IPreprocessBuildWithReport", "IPostprocessBuildWithReport",
    "BuildFailedException", "AssetPostprocessor", "AssetImporter",
    "TextAsset", "DefaultAsset",
}

KNOWN_EXTERNAL_TYPES = BCL_TYPES | UNITY_TYPES | TEST_TYPES | UNITY_EDITOR_TYPES

# C# contextual/reserved words that can appear where the reference scanners look
# for a type name. They are never types, so they must never be reported.
CSHARP_KEYWORDS = {
    "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
    "checked", "class", "const", "continue", "decimal", "default", "delegate",
    "do", "double", "else", "enum", "event", "explicit", "extern", "false",
    "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
    "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
    "new", "null", "object", "operator", "out", "override", "params", "private",
    "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
    "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
    "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
    "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    "var", "dynamic", "nameof", "when", "where", "yield", "async", "await",
    "value", "get", "set", "add", "remove", "init", "record", "partial",
    "nint", "nuint", "notnull", "unmanaged", "managed", "global",
}


# ---------------------------------------------------------------------------
# Reporting
# ---------------------------------------------------------------------------

class Report:
    """Collects errors and warnings so every check can run before anything exits."""

    def __init__(self, quiet=False):
        self.errors = []
        self.warnings = []
        self.quiet = quiet
        self.checks_run = 0

    def error(self, check, path, line, message):
        self.errors.append((check, path, line, message))

    def warn(self, check, path, line, message):
        self.warnings.append((check, path, line, message))

    def emit(self):
        for kind, items in (("ERROR", self.errors), ("WARN ", self.warnings)):
            if kind.strip() == "WARN" and self.quiet:
                continue
            for check, path, line, message in items:
                where = path if line is None else "%s:%d" % (path, line)
                print("%s [%s] %s: %s" % (kind, check, where, message))


def fail(message):
    print("FATAL: %s" % message, file=sys.stderr)
    sys.exit(2)


# ---------------------------------------------------------------------------
# The lexer
# ---------------------------------------------------------------------------

class ScanResult:
    """The three views of a source file that every other check reads.

    ``code``            comments AND string/char literals blanked to spaces.
                        Safe to run regexes over: a brace in a string cannot
                        reach it, and neither can the word ``class`` in a doc
                        comment.
    ``code_with_strings`` comments blanked, literals intact. Needed by the
                        engine-free check, which must still catch
                        ``Type.GetType("UnityEngine.Vector3")``, and by the
                        LocKey scanner, which is looking for literals.
    ``literals``        every string literal as (value, offset).

    All three keep the original file's length and line breaks, so any offset
    maps back to a real line number.
    """

    def __init__(self, code, code_with_strings, literals, errors, line_starts):
        self.code = code
        self.code_with_strings = code_with_strings
        self.literals = literals
        self.errors = errors
        self.line_starts = line_starts

    def line_of(self, offset):
        """1-based line number for a character offset. Binary search."""
        low, high = 0, len(self.line_starts) - 1
        while low < high:
            mid = (low + high + 1) // 2
            if self.line_starts[mid] <= offset:
                low = mid
            else:
                high = mid - 1
        return low + 1

    def column_of(self, offset):
        return offset - self.line_starts[self.line_of(offset) - 1] + 1


class CSharpScanner:
    """Turns C# source into the views above.

    WHY a real scanner and not ``text.count('{')``: this project's sources
    contain braces inside string literals (``"#" + key + "#"`` is tame; the
    JSON writer and the ``#key#`` fallback are not), the word ``class`` inside
    XML doc comments, and ``'"'`` char literals. A counter gets every one of
    those wrong, and a check that cries wolf is a check people stop reading.

    Handles: line and block comments, regular strings with backslash escapes,
    verbatim strings (``@"..."`` with ``""`` escapes), interpolated strings
    (``$"..."``, ``$@"..."``, ``@$"..."``) including ``{{``/``}}`` escapes and
    arbitrarily nested holes, and char literals.
    """

    def __init__(self, text):
        self.text = text
        self.n = len(text)
        self.pos = 0
        self.code = []
        self.with_strings = []
        self.literals = []
        self.errors = []

    # -- emit helpers -------------------------------------------------------

    def _blank(self, ch):
        """Consume a character that belongs to a comment: gone from both views."""
        filler = "\n" if ch == "\n" else " "
        self.code.append(filler)
        self.with_strings.append(filler)

    def _literal_char(self, ch):
        """Consume a character inside a literal: blanked in code, kept verbatim."""
        self.code.append("\n" if ch == "\n" else " ")
        self.with_strings.append(ch)

    def _code_char(self, ch):
        self.code.append(ch)
        self.with_strings.append(ch)

    # -- main loop ----------------------------------------------------------

    def scan(self):
        text = self.text
        while self.pos < self.n:
            ch = text[self.pos]

            if ch == "/" and self.pos + 1 < self.n and text[self.pos + 1] == "/":
                self._skip_line_comment()
                continue

            if ch == "/" and self.pos + 1 < self.n and text[self.pos + 1] == "*":
                self._skip_block_comment()
                continue

            if ch in "\"@$" and self._string_starts_here():
                self._read_string()
                continue

            if ch == "'":
                self._read_char_literal()
                continue

            if ch == "#" and self._at_line_start():
                self._read_preprocessor_directive()
                continue

            self._code_char(ch)
            self.pos += 1

        line_starts = [0]
        for index, ch in enumerate(self.text):
            if ch == "\n":
                line_starts.append(index + 1)

        return ScanResult(
            "".join(self.code),
            "".join(self.with_strings),
            self.literals,
            self.errors,
            line_starts,
        )

    def _skip_line_comment(self):
        while self.pos < self.n and self.text[self.pos] != "\n":
            self._blank(self.text[self.pos])
            self.pos += 1

    def _skip_block_comment(self):
        start = self.pos
        self._blank("/")
        self._blank("*")
        self.pos += 2
        while self.pos < self.n:
            if self.text[self.pos] == "*" and self.pos + 1 < self.n and self.text[self.pos + 1] == "/":
                self._blank("*")
                self._blank("/")
                self.pos += 2
                return
            self._blank(self.text[self.pos])
            self.pos += 1
        self.errors.append((start, "unterminated block comment"))

    def _at_line_start(self):
        """True when only whitespace precedes pos on this line."""
        i = self.pos - 1
        while i >= 0 and self.text[i] in " \t":
            i -= 1
        return i < 0 or self.text[i] == "\n"

    def _read_preprocessor_directive(self):
        """Consumes a `#if`/`#endif`/`#region`/... line.

        WHY it is dropped from the code view but kept in the string view:
        `#if UNITY_EDITOR` is a compile-time symbol, not a type, and leaving it
        in would make the reference scanner read `UNITY_EDITOR` followed by the
        next line's first identifier as a field declaration. Check C still sees
        it, because a `#if UNITY_EDITOR` block inside Core is exactly the kind
        of engine coupling that check exists to refuse.

        Directive lines carry no brackets of their own, so removing them cannot
        unbalance a file. An `#if`/`#else` pair whose two branches open a
        different number of braces WOULD produce a false balance error -- it is
        also unreadable code and nothing in this project does it.
        """
        while self.pos < self.n and self.text[self.pos] != "\n":
            ch = self.text[self.pos]
            self.code.append(" ")
            self.with_strings.append(ch)
            self.pos += 1

    def _string_starts_here(self):
        """True when a string literal (with any prefix combination) opens at pos."""
        i = self.pos
        seen_quote = False
        while i < self.n and self.text[i] in "@$":
            i += 1
        if i < self.n and self.text[i] == '"':
            seen_quote = True
        return seen_quote

    def _read_string(self):
        start = self.pos
        verbatim = False
        interpolated = False

        while self.text[self.pos] in "@$":
            if self.text[self.pos] == "@":
                verbatim = True
            else:
                interpolated = True
            self._literal_char(self.text[self.pos])
            self.pos += 1

        # Opening quote.
        self._literal_char('"')
        self.pos += 1

        value = []
        while self.pos < self.n:
            ch = self.text[self.pos]

            if verbatim:
                if ch == '"':
                    if self.pos + 1 < self.n and self.text[self.pos + 1] == '"':
                        value.append('"')
                        self._literal_char('"')
                        self._literal_char('"')
                        self.pos += 2
                        continue
                    self._literal_char('"')
                    self.pos += 1
                    self._record_literal(start, value)
                    return
            else:
                if ch == "\\":
                    # An escape sequence. Copy both characters; the exact escape
                    # does not matter because nothing downstream interprets the
                    # value beyond comparing it to a LocKey.
                    value.append(ch)
                    self._literal_char(ch)
                    self.pos += 1
                    if self.pos < self.n:
                        value.append(self.text[self.pos])
                        self._literal_char(self.text[self.pos])
                        self.pos += 1
                    continue
                if ch == '"':
                    self._literal_char('"')
                    self.pos += 1
                    self._record_literal(start, value)
                    return
                if ch == "\n":
                    # A non-verbatim string cannot span lines. Stop here rather
                    # than swallowing the rest of the file.
                    self.errors.append((start, "unterminated string literal"))
                    self._record_literal(start, value)
                    return

            if interpolated and ch in "{}":
                nxt = self.text[self.pos + 1] if self.pos + 1 < self.n else ""
                if nxt == ch:
                    # {{ or }} -- an escaped brace, part of the text.
                    value.append(ch)
                    self._literal_char(ch)
                    self._literal_char(ch)
                    self.pos += 2
                    continue
                if ch == "{":
                    self._read_interpolation_hole()
                    continue

            value.append(ch)
            self._literal_char(ch)
            self.pos += 1

        self.errors.append((start, "unterminated string literal"))
        self._record_literal(start, value)

    def _read_interpolation_hole(self):
        """Consumes ``{ ... }`` inside an interpolated string, keeping the code.

        The braces themselves are emitted into the code view so the balance
        check sees a matched pair, and the expression between them is scanned
        normally -- which is what lets check E see a type named only inside an
        interpolated string.
        """
        self._code_char("{")
        self.pos += 1
        depth = 1
        while self.pos < self.n and depth > 0:
            ch = self.text[self.pos]

            if ch in "\"@$" and self._string_starts_here():
                self._read_string()
                continue
            if ch == "'":
                self._read_char_literal()
                continue
            if ch == "/" and self.pos + 1 < self.n and self.text[self.pos + 1] == "/":
                self._skip_line_comment()
                continue
            if ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    self._code_char("}")
                    self.pos += 1
                    return

            self._code_char(ch)
            self.pos += 1

    def _read_char_literal(self):
        start = self.pos
        self._literal_char("'")
        self.pos += 1
        while self.pos < self.n:
            ch = self.text[self.pos]
            if ch == "\\":
                self._literal_char(ch)
                self.pos += 1
                if self.pos < self.n:
                    self._literal_char(self.text[self.pos])
                    self.pos += 1
                continue
            self._literal_char(ch)
            self.pos += 1
            if ch == "'":
                return
            if ch == "\n":
                self.errors.append((start, "unterminated character literal"))
                return
        self.errors.append((start, "unterminated character literal"))

    def _record_literal(self, start, value_chars):
        self.literals.append(("".join(value_chars), start))


# ---------------------------------------------------------------------------
# File discovery
# ---------------------------------------------------------------------------

SKIP_DIRECTORIES = {".git", "Library", "Temp", "Obj", "obj", "bin", "Build", "Builds", "Logs", "UserSettings"}


def find_files(root, extension):
    found = []
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = sorted(d for d in dirnames if d not in SKIP_DIRECTORIES)
        for name in sorted(filenames):
            if name.endswith(extension):
                found.append(os.path.join(dirpath, name))
    return found


def read_text(path):
    try:
        with open(path, "r", encoding="utf-8-sig") as handle:
            return handle.read()
    except (IOError, OSError, UnicodeDecodeError) as exc:
        fail("cannot read %s: %s" % (path, exc))


def read_bytes(path):
    """Raw bytes, for comparisons that must see what read_text() normalises away.

    read_text() opens with utf-8-sig, so a BOM on one copy of a file and not the
    other is invisible to it -- and a BOM is exactly the kind of drift a
    spreadsheet editor introduces when a translator saves one of the two
    localization CSVs.
    """
    try:
        with open(path, "rb") as handle:
            return handle.read()
    except (IOError, OSError) as exc:
        fail("cannot read %s: %s" % (path, exc))


def first_difference(left, right):
    """(offset, 1-based line) of the first differing byte, or None if equal."""
    limit = min(len(left), len(right))
    offset = limit
    for index in range(limit):
        if left[index] != right[index]:
            offset = index
            break
    else:
        if len(left) == len(right):
            return None

    return offset, left[:offset].count(b"\n") + 1


def relative(root, path):
    return os.path.relpath(path, root).replace(os.sep, "/")


# ---------------------------------------------------------------------------
# Check A -- bracket balance
# ---------------------------------------------------------------------------

OPENERS = {"(": ")", "[": "]", "{": "}"}
CLOSERS = {")": "(", "]": "[", "}": "{"}


def check_balance(report, rel_path, scan):
    """Every (), [] and {} closes, in order, outside strings and comments."""
    for offset, message in scan.errors:
        report.error("BALANCE", rel_path, scan.line_of(offset), message)

    stack = []
    for offset, ch in enumerate(scan.code):
        if ch in OPENERS:
            stack.append((ch, offset))
        elif ch in CLOSERS:
            if not stack:
                report.error(
                    "BALANCE", rel_path, scan.line_of(offset),
                    "stray '%s' at column %d with nothing open" % (ch, scan.column_of(offset)))
                return
            opener, open_offset = stack.pop()
            if OPENERS[opener] != ch:
                report.error(
                    "BALANCE", rel_path, scan.line_of(offset),
                    "'%s' closes '%s' opened on line %d; expected '%s'"
                    % (ch, opener, scan.line_of(open_offset), OPENERS[opener]))
                return

    for opener, open_offset in stack:
        report.error(
            "BALANCE", rel_path, scan.line_of(open_offset),
            "'%s' opened at column %d is never closed" % (opener, scan.column_of(open_offset)))


# ---------------------------------------------------------------------------
# Declaration and namespace parsing (shared by checks B, E and G)
# ---------------------------------------------------------------------------

NAMESPACE_RE = re.compile(r"\bnamespace\s+([A-Za-z_][\w.]*)\s*([;{])")

TYPE_DECL_RE = re.compile(
    r"(?P<mods>(?:\b(?:public|internal|protected|private|static|sealed|abstract|partial|readonly|ref|unsafe|new)\b\s+)*)"
    r"\b(?P<kind>class|struct|interface|enum|record)\b\s+"
    r"(?P<name>[A-Za-z_]\w*)"
    r"(?P<generics>\s*<[^<>{}();=]*>)?"
    r"(?P<rest>[^{;]*)"
)

DELEGATE_DECL_RE = re.compile(
    r"(?P<mods>(?:\b(?:public|internal|protected|private|static|unsafe|new)\b\s+)*)"
    r"\bdelegate\b\s+[\w<>\[\],.\s?]+?\s+(?P<name>[A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\("
)


class Declaration:
    def __init__(self, kind, name, qualified, namespace, offset, is_public, is_partial, type_parameters, base_list):
        self.kind = kind
        self.name = name
        self.qualified = qualified
        self.namespace = namespace
        self.offset = offset
        self.is_public = is_public
        self.is_partial = is_partial
        self.type_parameters = type_parameters
        self.base_list = base_list


def depth_prefix(code):
    """depth[i] = brace nesting depth immediately before character i."""
    depths = []
    depth = 0
    for ch in code:
        depths.append(depth)
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
    depths.append(depth)
    return depths


def split_type_parameters(text):
    if not text:
        return []
    inner = text.strip()
    if inner.startswith("<"):
        inner = inner[1:]
    if inner.endswith(">"):
        inner = inner[:-1]
    names = []
    for part in inner.split(","):
        part = part.strip().split()[-1] if part.strip() else ""
        if part and re.match(r"^[A-Za-z_]\w*$", part):
            names.append(part)
    return names


def parse_declarations(code):
    """Namespaces and type declarations, with nesting resolved by brace depth."""
    depths = depth_prefix(code)
    events = []

    for match in NAMESPACE_RE.finditer(code):
        events.append((match.start(), "namespace", match))
    for match in TYPE_DECL_RE.finditer(code):
        events.append((match.start("kind"), "type", match))
    for match in DELEGATE_DECL_RE.finditer(code):
        events.append((match.start("name"), "delegate", match))

    events.sort(key=lambda item: item[0])

    namespaces = []
    declarations = []
    stack = []  # (depth, kind, name)

    for offset, kind, match in events:
        depth = depths[offset]
        while stack and stack[-1][0] >= depth:
            stack.pop()

        if kind == "namespace":
            name = match.group(1)
            namespaces.append((name, offset))
            # A file-scoped namespace ("namespace X;") never closes, so it is
            # pushed below every brace depth and stays for the whole file.
            stack.append((-1 if match.group(2) == ";" else depth, "namespace", name))
            continue

        if kind == "delegate":
            name = match.group("name")
            mods = match.group("mods") or ""
            ns = ".".join(entry[2] for entry in stack if entry[1] == "namespace")
            outer = [entry[2] for entry in stack if entry[1] == "type"]
            qualified = ".".join(outer + [name])
            declarations.append(Declaration(
                "delegate", name, qualified, ns, offset,
                "public" in mods.split(), False, [], []))
            continue

        name = match.group("name")
        mods = (match.group("mods") or "").split()
        rest = match.group("rest") or ""
        base_list = []
        if ":" in rest:
            after = rest.split(":", 1)[1]
            after = re.split(r"\bwhere\b", after)[0]
            base_list = [part.strip() for part in split_top_level(after) if part.strip()]

        ns = ".".join(entry[2] for entry in stack if entry[1] == "namespace")
        outer = [entry[2] for entry in stack if entry[1] == "type"]
        qualified = ".".join(outer + [name])

        declarations.append(Declaration(
            match.group("kind"), name, qualified, ns, offset,
            "public" in mods, "partial" in mods,
            split_type_parameters(match.group("generics")), base_list))
        stack.append((depth, "type", name))

    return namespaces, declarations


def split_top_level(text):
    """Splits on commas that are not inside <>, () or []."""
    parts = []
    depth = 0
    current = []
    for ch in text:
        if ch in "<([":
            depth += 1
        elif ch in ">)]":
            depth -= 1
        if ch == "," and depth <= 0:
            parts.append("".join(current))
            current = []
            continue
        current.append(ch)
    parts.append("".join(current))
    return parts


# ---------------------------------------------------------------------------
# Check B -- namespace matches directory
# ---------------------------------------------------------------------------

def check_namespaces(report, root, path, rel_path, namespaces):
    rule = None
    for directory, prefix in NAMESPACE_RULES:
        if path.startswith(os.path.join(root, directory) + os.sep):
            rule = prefix
            break
    if rule is None:
        return

    if not namespaces:
        report.error("NAMESPACE", rel_path, 1, "no namespace declared; expected one starting '%s'" % rule)
        return

    for name, offset in namespaces:
        if name in NAMESPACE_EXEMPTIONS:
            continue
        if name != rule and not name.startswith(rule + "."):
            report.error("NAMESPACE", rel_path, None, "namespace '%s' must start with '%s'" % (name, rule))


# ---------------------------------------------------------------------------
# Check C -- Core is engine free
# ---------------------------------------------------------------------------

ENGINE_TOKEN_RE = re.compile(r"\b(UnityEngine|UnityEditor)\b")


def check_engine_free(report, rel_path, scan):
    """No mention of the engine under Scripts/Core, in code OR in a literal.

    Literals are searched too because ``Type.GetType("UnityEngine.Vector3")`` is
    the one way to smuggle the engine into Core past the assembly definition --
    and it is the only way that would not already be caught by the Editor.
    Comments are excluded: a doc comment stating the rule is not a breach of it.
    """
    for match in ENGINE_TOKEN_RE.finditer(scan.code_with_strings):
        report.error(
            "CORE-ENGINE", rel_path, scan.line_of(match.start()),
            "Core must not name '%s' (ADR-0002)" % match.group(1))


# ---------------------------------------------------------------------------
# Check D -- assembly definitions
# ---------------------------------------------------------------------------

def check_asmdefs(report, root, asmdef_paths):
    for path in asmdef_paths:
        rel_path = relative(root, path)
        raw = read_text(path)
        try:
            data = json.loads(raw)
        except ValueError as exc:
            report.error("ASMDEF", rel_path, None, "not valid JSON: %s" % exc)
            continue

        if not isinstance(data, dict):
            report.error("ASMDEF", rel_path, None, "top level must be a JSON object")
            continue

        expected = os.path.basename(path)[: -len(".asmdef")]
        name = data.get("name")
        if name != expected:
            report.error("ASMDEF", rel_path, None,
                         "\"name\" is %r but the file is named %r; Unity matches on the field and "
                         "every reference to the wrong one silently resolves to nothing"
                         % (name, expected))

        references = data.get("references", [])
        if not isinstance(references, list):
            report.error("ASMDEF", rel_path, None, "\"references\" must be an array")
            references = []

        if name == CORE_ASMDEF_NAME:
            if data.get("noEngineReferences") is not True:
                report.error("ASMDEF", rel_path, None,
                             "Core must set \"noEngineReferences\": true (ADR-0002)")
            if references:
                report.error("ASMDEF", rel_path, None,
                             "Core must have an empty \"references\" array (ADR-0001); found %s"
                             % ", ".join(str(item) for item in references))

        # An empty includePlatforms means "every platform", which is what a
        # runtime assembly and a PlayMode test assembly both need. ["Editor"] is
        # the one deliberate exception: an EditMode test assembly cannot ship.
        include_platforms = data.get("includePlatforms")
        if include_platforms not in (None, [], ["Editor"]):
            report.warn("ASMDEF", rel_path, None,
                        "\"includePlatforms\" is %s, so this assembly is excluded from some player "
                        "builds; only [] (everywhere) and [\"Editor\"] (EditMode tests) are expected"
                        % include_platforms)


# ---------------------------------------------------------------------------
# Check E -- contract drift
# ---------------------------------------------------------------------------
#
# Only unambiguous type positions are scanned: base types, field/parameter/local
# declarations, generic arguments, `new`, `typeof`, `is` and `as`. Member access
# (`Something.Member`) is deliberately NOT scanned -- a PascalCase property
# reads identically to a static class, and the false positives would bury the
# real finding.

NEW_RE = re.compile(r"\bnew\s+(?P<type>[A-Za-z_][\w]*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*(?=[<(\[{])")
TYPEOF_RE = re.compile(r"\btypeof\s*\(\s*(?P<type>[A-Za-z_][\w.]*)")
IS_AS_RE = re.compile(r"\b(?:is|as)\s+(?P<type>[A-Z][\w.]*)")
GENERIC_ARGS_RE = re.compile(r"[A-Za-z_]\w*\s*<(?P<args>[\w\s,.\[\]?]*)>")
DECLARATION_RE = re.compile(
    r"(?<![\w.])(?P<type>[A-Z][\w]*(?:\.[A-Za-z_]\w*)*)"
    r"(?P<generics><[\w\s,.\[\]?]*>)?"
    r"(?P<array>(?:\[\s*\])*)"
    r"\s+(?P<name>@?[A-Za-z_]\w*)\s*(?=[;,=)\{])"
)


def simple_type_name(raw):
    """Reduces 'System.Collections.Generic.List<Foo>[]?' to 'List'."""
    name = raw.strip()
    name = re.sub(r"\s+", "", name)
    name = name.split("<")[0]
    name = name.replace("[]", "").rstrip("?*&")
    if not name:
        return ""
    return name.split(".")[-1]


def collect_type_references(code):
    """Yields (simple_name, offset) for every unambiguous type position."""
    references = []

    for match in NEW_RE.finditer(code):
        references.append((simple_type_name(match.group("type")), match.start("type")))

    for match in TYPEOF_RE.finditer(code):
        references.append((simple_type_name(match.group("type")), match.start("type")))

    for match in IS_AS_RE.finditer(code):
        references.append((simple_type_name(match.group("type")), match.start("type")))

    for match in GENERIC_ARGS_RE.finditer(code):
        args = match.group("args")
        for part in split_top_level(args):
            name = simple_type_name(part)
            if name and name[0].isupper():
                references.append((name, match.start("args")))

    for match in DECLARATION_RE.finditer(code):
        name = simple_type_name(match.group("type"))
        if name:
            references.append((name, match.start("type")))
        for part in split_top_level((match.group("generics") or "").strip("<>")):
            inner = simple_type_name(part)
            if inner and inner[0].isupper():
                references.append((inner, match.start("type")))

    return references


def looks_like_type_parameter(name):
    """T, TCommand, TSignal -- the project's generic parameters, by convention."""
    return bool(re.match(r"^T([A-Z]\w*)?$", name))


def check_contract_drift(report, files, declared_names):
    """Reports any referenced type that nothing in the project declares."""
    reported = set()
    for rel_path, scan, _namespaces, declarations in files:
        local_type_parameters = set()
        for declaration in declarations:
            local_type_parameters.update(declaration.type_parameters)

        # Base type lists are a type position the generic scanners above miss.
        references = collect_type_references(scan.code)
        for declaration in declarations:
            for base in declaration.base_list:
                name = simple_type_name(base)
                if name:
                    references.append((name, declaration.offset))
                for part in split_top_level(base[base.find("<") + 1:].rstrip(">") if "<" in base else ""):
                    inner = simple_type_name(part)
                    if inner and inner[0].isupper():
                        references.append((inner, declaration.offset))

        for name, offset in references:
            if not name or name in CSHARP_KEYWORDS or not name[0].isupper():
                continue
            if name in declared_names or name in KNOWN_EXTERNAL_TYPES:
                continue
            if name in local_type_parameters or looks_like_type_parameter(name):
                continue
            line = scan.line_of(offset)
            key = (rel_path, line, name)
            if key in reported:
                continue
            reported.add(key)
            report.error(
                "CONTRACT", rel_path, line,
                "type '%s' is referenced but never declared in this project and is not a known "
                "BCL/Unity/NUnit type" % name)


# ---------------------------------------------------------------------------
# Check F -- LocKeys
# ---------------------------------------------------------------------------

LOCKEY_LITERAL_RE = re.compile(r"\bnew\s+LocKey\s*\(\s*\"(?P<key>(?:[^\"\\\n]|\\.)*)\"\s*\)")


def parse_csv_table(text, rel_path, report):
    """Mirror of ForgottenIsle.Core.Data.CsvTableParser, in Python.

    Kept deliberately close to the C# so that "the validator sees this key" and
    "the game sees this key" cannot drift apart: quoted fields, doubled quotes,
    whole-line '#' comments, blank lines, CRLF.
    """
    keys = {}
    position = 0
    length = len(text)

    def read_field():
        nonlocal position
        buffer = []
        if position < length and text[position] == '"':
            position += 1
            while position < length:
                ch = text[position]
                if ch == '"':
                    if position + 1 < length and text[position + 1] == '"':
                        buffer.append('"')
                        position += 2
                        continue
                    position += 1
                    return "".join(buffer), True
                buffer.append(ch)
                position += 1
            return "".join(buffer), False
        while position < length and text[position] not in ",\r\n":
            buffer.append(text[position])
            position += 1
        return "".join(buffer), True

    line_number = 1
    while position < length:
        # Skip blank lines and comments.
        while position < length:
            start_of_line = position
            end = position
            while end < length and text[end] not in "\r\n":
                end += 1
            raw = text[start_of_line:end].strip()
            if raw == "" or raw.startswith("#"):
                position = end
                while position < length and text[position] in "\r\n":
                    if text[position] == "\n":
                        line_number += 1
                    position += 1
                continue
            break

        if position >= length:
            break

        current_line = line_number
        key, closed = read_field()
        if not closed:
            report.error("LOCKEY", rel_path, current_line, "unterminated quoted field")
            break

        if position < length and text[position] == ",":
            position += 1
            _value, closed = read_field()
            if not closed:
                report.error("LOCKEY", rel_path, current_line, "unterminated quoted value")
                break
        else:
            report.error("LOCKEY", rel_path, current_line,
                         "row has no comma, so it defines no value: %r" % key)

        key = key.strip()
        if key:
            if key in keys:
                report.warn("LOCKEY", rel_path, current_line,
                            "duplicate key '%s' (first seen on line %d); the later row wins"
                            % (key, keys[key]))
            keys[key] = current_line

        while position < length and text[position] in "\r\n":
            if text[position] == "\n":
                line_number += 1
            position += 1

    return keys


def is_test_file(rel_path):
    return rel_path.startswith(TESTS_DIR.replace(os.sep, "/") + "/")


def check_lockeys(report, root, files, csv_keys, csv_rel_path):
    """Every shipped LocKey literal resolves; every shipped row is reachable.

    Test sources are excluded in both directions on purpose. A localization test
    must construct keys that deliberately do NOT exist -- proving the `#key#`
    fallback fires is most of what there is to test -- so requiring them would
    make this check permanently red. In the other direction, a row used only by
    a test is a row no player ever sees, so it still counts as unused.
    """
    used = {}
    non_canonical = []
    fragments = set()

    for rel_path, scan, _namespaces, _declarations in files:
        if is_test_file(rel_path):
            continue

        for match in LOCKEY_LITERAL_RE.finditer(scan.code_with_strings):
            key = match.group("key")
            line = scan.line_of(match.start())
            if CANONICAL_LOCKEY.match(key):
                used.setdefault(key, (rel_path, line))
            else:
                non_canonical.append((rel_path, line, key))

        # Prefixes and suffixes that code concatenates into a key at runtime --
        # "zone." + name + ".name". They cannot be resolved statically, so they
        # only suppress "unused" warnings; they never satisfy a missing key.
        for value, _offset in scan.literals:
            stripped = value.strip()
            if len(stripped) >= KEY_FRAGMENT_MIN_LENGTH and re.match(r"^[a-z0-9_.]+$", stripped) and "." in stripped:
                fragments.add(stripped)

    for key, (rel_path, line) in sorted(used.items()):
        if key not in csv_keys:
            report.error("LOCKEY", rel_path, line,
                         "LocKey \"%s\" has no row in %s; it will render in game as #%s#"
                         % (key, csv_rel_path, key))

    for rel_path, line, key in non_canonical:
        report.warn("LOCKEY", rel_path, line,
                    "LocKey \"%s\" is not lowercase dotted house style, so the missing-key check "
                    "cannot vouch for it%s"
                    % (key, "" if key in csv_keys else " -- and it is not in " + csv_rel_path))

    for key in sorted(csv_keys):
        if key in used:
            continue
        if any(key.startswith(fragment) or key.endswith(fragment) for fragment in fragments):
            continue
        report.warn("LOCKEY", csv_rel_path, csv_keys[key],
                    "key '%s' is never used by any LocKey literal (it may be composed at runtime)" % key)


def check_localization_mirror(report, root, authoring_keys):
    """The Resources copy is what a player build reads; a drift ships #key#."""
    mirror_path = os.path.join(root, LOCALIZATION_MIRROR_CSV)
    mirror_rel = LOCALIZATION_MIRROR_CSV.replace(os.sep, "/")
    if not os.path.isfile(mirror_path):
        report.error("LOCKEY", mirror_rel, None,
                     "missing: a player build loads the string table from Resources, not from "
                     "Assets/Localization")
        return

    # The two files are one file kept in two places on purpose: the authoring
    # copy is what a translator edits, the Resources copy is the only one a
    # player build can load. Nothing but a copy step keeps them equal, so an
    # edit applied to one and not the other is a silent ship of #key# text.
    # Compare bytes, not parsed keys: a drift in a VALUE, a comment, row order
    # or a BOM is just as wrong and the key comparison below cannot see any of
    # it.
    authoring_bytes = read_bytes(os.path.join(root, LOCALIZATION_CSV))
    mirror_bytes = read_bytes(mirror_path)
    difference = first_difference(authoring_bytes, mirror_bytes)
    if difference is not None:
        offset, line = difference
        report.error("LOCKEY", mirror_rel, None,
                     "is not byte-identical to %s (first difference at byte %d, line %d; "
                     "%d bytes here vs %d there) -- these two files must match exactly; "
                     "re-copy with: cp %s %s"
                     % (LOCALIZATION_CSV.replace(os.sep, "/"), offset, line,
                        len(mirror_bytes), len(authoring_bytes),
                        LOCALIZATION_CSV.replace(os.sep, "/"), mirror_rel))

    mirror_keys = parse_csv_table(read_text(mirror_path), mirror_rel, report)
    for key in sorted(authoring_keys):
        if key not in mirror_keys:
            report.error("LOCKEY", mirror_rel, None,
                         "key '%s' exists in the authoring table but not here; copy the file "
                         "(cp Assets/Localization/en.csv Assets/Resources/Localization/en.csv)" % key)
    for key in sorted(mirror_keys):
        if key not in authoring_keys:
            report.warn("LOCKEY", mirror_rel, mirror_keys[key],
                        "key '%s' exists only in the Resources copy; edits belong in the authoring "
                        "table" % key)


# ---------------------------------------------------------------------------
# Check G -- duplicate public types
# ---------------------------------------------------------------------------

def check_duplicate_types(report, files):
    seen = {}
    for rel_path, scan, _namespaces, declarations in files:
        for declaration in declarations:
            if not declaration.is_public:
                continue
            key = (declaration.namespace, declaration.qualified)
            seen.setdefault(key, []).append((rel_path, scan.line_of(declaration.offset), declaration))

    for (namespace, qualified), entries in sorted(seen.items()):
        if len(entries) < 2:
            continue
        if all(entry[2].is_partial for entry in entries):
            continue
        first_path, first_line, _ = entries[0]
        for rel_path, line, _ in entries[1:]:
            report.error(
                "DUPLICATE", rel_path, line,
                "public type '%s.%s' is already declared at %s:%d; one of them will not compile"
                % (namespace or "<global>", qualified, first_path, first_line))



def check_missing_usings(report, files):
    """A project type referenced without its namespace being in scope (C# CS0246).

    WHY THIS EXISTS: the contract-drift check only asks "is this type declared anywhere in the
    project", so a type that exists but whose namespace was never imported passes it cleanly and
    then fails in Unity as CS0246. That is exactly what shipped in GameContext.cs, which used
    SaveSlotService without `using ForgottenIsle.Game.Saves;`. It is also the single most likely
    error when several people (or agents) edit interlocking files without a compiler.
    """
    # type name -> set of namespaces declaring it
    homes = {}
    for _rel, _scan, _ns, declarations in files:
        for declaration in declarations:
            if declaration.namespace:
                homes.setdefault(declaration.name, set()).add(declaration.namespace)

    for rel_path, scan, namespaces, declarations in files:
        code = scan.code
        usings = set(re.findall(r"^\s*using\s+(?:static\s+)?([\w.]+)\s*;", code, re.M))

        # Namespaces that are in scope without an explicit using: the file's own namespaces and
        # every ancestor of them, because C# resolves outward through enclosing namespaces.
        in_scope = set(usings)
        for namespace, _offset in namespaces:
            parts = namespace.split(".")
            for index in range(len(parts)):
                in_scope.add(".".join(parts[: index + 1]))

        # Types declared in this file are reachable regardless of namespace (nested or same file).
        local = {declaration.name for declaration in declarations}

        reported = set()
        for name, offset in collect_type_references(code):
            if name in local or name in reported:
                continue
            owners = homes.get(name)
            if not owners:
                continue
            if owners & in_scope:
                continue
            # A fully-qualified reference (Some.Name.Space.Type) needs no using at all. The offset
            # points at the START of the written token, which simple_type_name() has already reduced
            # to its last segment -- so if the raw token still carries a dot, the author qualified it
            # deliberately and the check must stay quiet.
            raw = re.match(r"[\w.]+", code[offset:])
            if raw and "." in raw.group(0):
                continue
            # Only report when every declaring namespace is out of scope, and name the fix.
            reported.add(name)
            suggestion = sorted(owners)[0]
            report.error(
                "USING", rel_path, scan.line_of(offset),
                "'%s' is declared in %s but that namespace is not in scope here; "
                "add 'using %s;' (this is CS0246 in Unity)"
                % (name, " / ".join(sorted(owners)), suggestion))


def check_member_type_collisions(report, files):
    """A nested type and a member sharing one name inside the SAME type (C# CS0102).

    Shipped once already: SessionService declared both a `PlayerParticipant` property and a nested
    `PlayerParticipant` class, which Unity rejects outright.

    Owner resolution is by brace depth, not by regex proximity. A field of type `Severity` named
    `Severity` inside a DIFFERENT nested type is legal C#, and an earlier naive version of this
    check reported six of those. A check that cries wolf gets ignored, so it resolves the enclosing
    type properly or it does not report.
    """
    member_re = re.compile(
        r"^[ \t]*(?:public|internal|protected|private)"
        r"(?:\s+(?:static|readonly|virtual|override|abstract|new|async|extern|unsafe|sealed))*"
        r"\s+(?!class\b|struct\b|interface\b|enum\b|record\b|delegate\b)"
        r"[\w<>\[\],.?]+\s+(?P<name>[A-Za-z_]\w*)\s*(?:=>|\{|;|\()",
        re.M)

    for rel_path, scan, _namespaces, declarations in files:
        code = scan.code
        nested_by_owner = {}
        for declaration in declarations:
            if "." not in declaration.qualified:
                continue
            owner, _, nested = declaration.qualified.rpartition(".")
            nested_by_owner.setdefault(owner, set()).add(nested)
        if not nested_by_owner:
            continue

        depths = depth_prefix(code)
        ordered = sorted(declarations, key=lambda d: d.offset)

        for match in member_re.finditer(code):
            offset = match.start("name")
            name = match.group("name")
            depth = depths[offset]

            # The enclosing type is the nearest preceding declaration one brace level out.
            owner_decl = None
            for declaration in ordered:
                if declaration.offset >= offset:
                    break
                if depths[declaration.offset] == depth - 1:
                    owner_decl = declaration
            if owner_decl is None:
                continue

            if name in nested_by_owner.get(owner_decl.qualified, ()):  # same type, same name
                report.error(
                    "COLLISION", rel_path, scan.line_of(offset),
                    "'%s' is both a member and a nested type inside '%s' "
                    "(this is CS0102 in Unity; rename one of them)" % (name, owner_decl.qualified))



# Unity APIs this editor version reports as obsolete, and what to use instead. Each entry earned
# its place by actually appearing in the Console -- this table is a record of real findings, not a
# guess at what might be deprecated.
DEPRECATED_UNITY_APIS = [
    (re.compile(r"\bFindObjectsSortMode\b"),
     "FindObjectsSortMode is obsolete in Unity 6.x; use the FindObjectsByType overload "
     "that does not take a sort mode (CS0618)"),
    (re.compile(r"\bDEVELOPMENT_BUILD\b"),
     "the DEVELOPMENT_BUILD preprocessor symbol is deprecated; use DEBUG "
     "(defined in the editor and in development builds, absent in release) (UAC0009)"),
    (re.compile(r"\bFindObjectsOfType\b|\bFindObjectOfType\b"),
     "FindObjectOfType/FindObjectsOfType are obsolete; use FindAnyObjectByType/FindObjectsByType"),
    (re.compile(r"(?<![\w<])TreeViewState(?![\w<])|(?<![\w<])TreeViewItem(?![\w<])"),
     "the non-generic IMGUI TreeView types were deprecated in Unity 6.3 and are obsolete-as-error; "
     "use the TreeViewState<int> / TreeViewItem<int> generics"),
    (re.compile(r"\bexpectedControlType\s*:"),
     "InputActionSetupExtensions.AddAction has no 'expectedControlType' parameter; "
     "the named argument is 'expectedControlLayout' (CS1739)"),
]


def check_deprecated_unity_apis(report, files):
    """Unity APIs that compile today but are reported obsolete by 6000.6.

    These surface as warnings rather than errors, which is exactly why they need a gate: a warning
    scrolls past, and the next editor version turns it into CS0619. Every pattern here was observed
    in the Console on this project, not guessed.
    """
    for rel_path, scan, _namespaces, _declarations in files:
        # code_with_strings, not code: two of these patterns live in places the string-stripping
        # scanner blanks out -- DEVELOPMENT_BUILD appears inside Conditional("...") literals and on
        # #if preprocessor lines. Scanning the stripped source silently missed both.
        source = getattr(scan, "code_with_strings", None) or scan.code
        for pattern, message in DEPRECATED_UNITY_APIS:
            for match in pattern.finditer(source):
                report.error("DEPRECATED", rel_path, scan.line_of(match.start()),
                             "'%s': %s" % (match.group(0), message))


# ---------------------------------------------------------------------------
# Driver
# ---------------------------------------------------------------------------

def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Structural validator for the Vardholm C# sources. Substitutes for a compiler.")
    parser.add_argument("root", nargs="?", default=".", help="project root (default: current directory)")
    parser.add_argument("-q", "--quiet", action="store_true", help="print errors only, suppress warnings")
    parser.add_argument("--strict", action="store_true", help="treat warnings as errors for the exit code")
    args = parser.parse_args(argv)

    root = os.path.abspath(args.root)
    if not os.path.isdir(os.path.join(root, "Assets")):
        fail("%s is not a Unity project root (no Assets/ directory)" % root)

    report = Report(quiet=args.quiet)

    cs_paths = find_files(os.path.join(root, "Assets"), ".cs")
    asmdef_paths = find_files(os.path.join(root, "Assets"), ".asmdef")

    print("validate-structure.py -- structural validation without a compiler")
    print("root: %s" % root)
    print("")

    # --- Scan every file once; every check reads the same views. ------------
    files = []
    for path in cs_paths:
        rel_path = relative(root, path)
        scan = CSharpScanner(read_text(path)).scan()
        namespaces, declarations = parse_declarations(scan.code)
        files.append((rel_path, scan, namespaces, declarations))

    # --- A: balance --------------------------------------------------------
    report.checks_run += 1
    for rel_path, scan, _namespaces, _declarations in files:
        check_balance(report, rel_path, scan)

    # --- B: namespaces -----------------------------------------------------
    report.checks_run += 1
    for index, path in enumerate(cs_paths):
        rel_path, _scan, namespaces, _declarations = files[index]
        check_namespaces(report, root, path, rel_path, namespaces)

    # --- C: engine-free Core ----------------------------------------------
    report.checks_run += 1
    core_prefix = os.path.join(root, CORE_DIR) + os.sep
    core_file_count = 0
    for index, path in enumerate(cs_paths):
        if not path.startswith(core_prefix):
            continue
        core_file_count += 1
        rel_path, scan, _namespaces, _declarations = files[index]
        check_engine_free(report, rel_path, scan)

    # --- D: asmdefs --------------------------------------------------------
    report.checks_run += 1
    check_asmdefs(report, root, asmdef_paths)

    # --- E: contract drift -------------------------------------------------
    report.checks_run += 1
    declared_names = set()
    for _rel_path, _scan, _namespaces, declarations in files:
        for declaration in declarations:
            declared_names.add(declaration.name)
    check_contract_drift(report, files, declared_names)

    # --- F: LocKeys --------------------------------------------------------
    report.checks_run += 1
    csv_path = os.path.join(root, LOCALIZATION_CSV)
    csv_rel = LOCALIZATION_CSV.replace(os.sep, "/")
    if not os.path.isfile(csv_path):
        report.error("LOCKEY", csv_rel, None, "the authoring string table is missing")
        csv_keys = {}
    else:
        csv_keys = parse_csv_table(read_text(csv_path), csv_rel, report)
        check_lockeys(report, root, files, csv_keys, csv_rel)
        check_localization_mirror(report, root, csv_keys)

    # --- G: duplicate public types ----------------------------------------
    report.checks_run += 1
    check_duplicate_types(report, files)
    report.checks_run += 1

    check_missing_usings(report, files)
    report.checks_run += 1

    check_member_type_collisions(report, files)
    report.checks_run += 1

    check_deprecated_unity_apis(report, files)

    # --- Output ------------------------------------------------------------
    report.emit()

    public_types = sum(
        1 for _rel, _scan, _ns, declarations in files
        for declaration in declarations if declaration.is_public)

    print("")
    print("summary")
    print("  C# files scanned .......... %d (%d under Scripts/Core)" % (len(cs_paths), core_file_count))
    print("  assembly definitions ...... %d" % len(asmdef_paths))
    print("  public types declared ..... %d" % public_types)
    print("  localization keys ......... %d" % len(csv_keys))
    print("  checks run ................ %d (balance, namespaces, engine-free core, asmdefs,"
          % report.checks_run)
    print("                                 contract drift, lockeys, duplicate types,")
    print("                                 missing usings, member/type collisions,")
    print("                                 deprecated Unity APIs)")
    print("  errors .................... %d" % len(report.errors))
    print("  warnings .................. %d%s" % (len(report.warnings), " (hidden by --quiet)" if args.quiet and report.warnings else ""))
    print("")

    if report.errors:
        print("FAILED: %d error(s). Nothing here is a style opinion -- each one is something that "
              "would not compile or would break at runtime." % len(report.errors))
        return 1

    if args.strict and report.warnings:
        print("FAILED: --strict, and %d warning(s) were reported." % len(report.warnings))
        return 1

    print("OK: no structural errors.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
