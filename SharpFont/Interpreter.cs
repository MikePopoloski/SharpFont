using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace SharpFont {
    class GraphicsState {
        public bool AutoFlip = true;
        public int DeltaBase = 9;
        public int DeltaShift = 3;
        public Vector2 DualProjection;
        public Vector2 Freedom = Vector2.UnitX;
        public int Gep0 = 1;
        public int Gep1 = 1;
        public int Gep2 = 1;
        public int InstructionControl = 0;
        public int Loop = 1;
        public int MinDistance = 1;
        public Vector2 Projection = Vector2.UnitX;
        public int RoundState = 1;
        public int Rp0 = 0;
        public int Rp1 = 0;
        public int Rp2 = 0;
        public bool ScanControl = false;
        public int SingleWidthCutIn = 0;
        public int SingleWidthValue = 0;
    }

    class ExecutionStack {
        int[] s;
        int count;

        public ExecutionStack (int maxStack) {
            s = new int[maxStack];
        }

        public int Peek () => Peek(count - 1);
        public bool PopBool () => Pop() != 0;
        public void Push (bool value) => Push(value ? 1 : 0);

        public void Clear () => count = 0;
        public void Depth () => Push(count);
        public void Duplicate () => Push(Peek());
        public void Copy () => Copy(Pop() - 1);
        public void Copy (int index) => Push(Peek(index));
        public void Move () => Move(Pop() - 1);
        public void Roll () => Move(2);

        public void Move (int index) {
            var val = Peek(index);
            for (int i = index; i < count - 1; i++)
                s[i] = s[i + 1];
            s[count - 1] = val;
        }

        public void Swap () {
            if (count < 2)
                throw new InvalidFontException();

            var tmp = s[count - 1];
            s[count - 1] = s[count - 2];
            s[count - 2] = tmp;
        }

        public void Push (int value) {
            if (count == s.Length)
                throw new InvalidFontException();
            s[count++] = value;
        }

        public int Pop () {
            if (count == 0)
                throw new InvalidFontException();
            return s[--count];
        }

        public int Peek (int index) {
            if (index < 0 || index >= count)
                throw new InvalidFontException();
            return s[index];
        }
    }

    struct FunctionDef {
        public byte[] Instructions;
        public int Offset;

        public FunctionDef (byte[] instructions, int offset) {
            Instructions = instructions;
            Offset = offset;
        }
    }

    class Interpreter {
        GraphicsState state;
        ExecutionStack stack;
        FunctionDef[] functions;
        float[] controlValueTable;
        byte[] instructions;
        int[] storage;
        float scale;
        int ppem;
        int ip;
        int callStackSize;
        Zone zp0, zp1, zp2;

        public Interpreter (int maxStack, int maxStorage, int maxFunctions) {
            stack = new ExecutionStack(maxStack);
            storage = new int[maxStorage];
            functions = new FunctionDef[maxFunctions];
            state = new GraphicsState();
        }

        public void SetControlValueTable (FUnit[] cvt, float scale, float ppem, byte[] cvProgram) {
            if (this.scale == scale || cvt == null)
                return;

            if (controlValueTable == null)
                controlValueTable = new float[cvt.Length];

            this.scale = scale;
            this.ppem = (int)Math.Round(ppem);

            for (int i = 0; i < cvt.Length; i++)
                controlValueTable[i] = cvt[i] * scale;

            if (cvProgram != null)
                Execute(cvProgram);
        }

        public void Execute (byte[] instructions) => Execute(instructions, 0, false);

        void Execute (byte[] instructions, int offset, bool inFunction) {
            this.instructions = instructions;
            ip = offset;

            // dispatch each instruction in the stream
            var length = instructions.Length;
            while (ip < length) {
                var opcode = NextOpCode();
                DebugPrint(opcode);
                switch (opcode) {
                    // ==== PUSH INSTRUCTIONS ====
                    case OpCode.NPUSHB: PushBytes(NextByte()); break;
                    case OpCode.NPUSHW: PushWords(NextByte()); break;
                    case OpCode.PUSHB1:
                    case OpCode.PUSHB2:
                    case OpCode.PUSHB3:
                    case OpCode.PUSHB4:
                    case OpCode.PUSHB5:
                    case OpCode.PUSHB6:
                    case OpCode.PUSHB7: // these instructions hardcode the number of bytes to push
                    case OpCode.PUSHB8: PushBytes(opcode - OpCode.PUSHB1 + 1); break;
                    case OpCode.PUSHW1:
                    case OpCode.PUSHW2:
                    case OpCode.PUSHW3:
                    case OpCode.PUSHW4:
                    case OpCode.PUSHW5:
                    case OpCode.PUSHW6:
                    case OpCode.PUSHW7: // these instructions hardcode the number of words to push
                    case OpCode.PUSHW8: PushWords(opcode - OpCode.PUSHW1 + 1); break;

                    // ==== STORAGE MANAGEMENT ====
                    case OpCode.RS:
                        {
                            var loc = stack.Pop();
                            CheckIndex(loc, storage.Length);
                            stack.Push(storage[loc]);
                        }
                        break;
                    case OpCode.WS:
                        {
                            var value = stack.Pop();
                            var loc = stack.Pop();
                            CheckIndex(loc, storage.Length);
                            storage[loc] = value;
                        }
                        break;

                    // ==== CONTROL VALUE TABLE ====
                    case OpCode.WCVTP:
                        {
                            var value = stack.Pop();
                            var loc = stack.Pop();
                            CheckIndex(loc, controlValueTable.Length);
                            controlValueTable[loc] = F26Dot6ToFloat(value);
                        }
                        break;
                    case OpCode.WCVTF:
                        {
                            var value = stack.Pop();
                            var loc = stack.Pop();
                            CheckIndex(loc, controlValueTable.Length);
                            controlValueTable[loc] = value * scale;
                        }
                        break;
                    case OpCode.RCVT:
                        {
                            var loc = stack.Pop();
                            CheckIndex(loc, controlValueTable.Length);
                            stack.Push(FloatToF26Dot6(controlValueTable[loc]));
                        }
                        break;

                    // ==== STATE VECTORS ====
                    case OpCode.SVTCA0:
                    case OpCode.SVTCA1:
                        {
                            var axis = opcode - OpCode.SVTCA0;
                            SetFreedomVectorToAxis(axis);
                            SetProjectionVectorToAxis(axis);
                        }
                        break;
                    case OpCode.SFVTPV: state.Freedom = state.Projection; break;
                    case OpCode.SPVTCA0:
                    case OpCode.SPVTCA1: SetProjectionVectorToAxis(opcode - OpCode.SPVTCA0); break;
                    case OpCode.SFVTCA0:
                    case OpCode.SFVTCA1: SetFreedomVectorToAxis(opcode - OpCode.SFVTCA0); break;
                    case OpCode.SPVTL0:
                    case OpCode.SPVTL1:
                    case OpCode.SFVTL0:
                    case OpCode.SFVTL1: SetVectorToLine(opcode - OpCode.SPVTL0, false); break;
                    case OpCode.SDPVTL0:
                    case OpCode.SDPVTL1: SetVectorToLine(opcode - OpCode.SDPVTL0, true); break;
                    case OpCode.SPVFS:
                    case OpCode.SFVFS:
                        {
                            var vec = Vector2.Normalize(new Vector2(
                                F2Dot14ToFloat(stack.Pop()),
                                F2Dot14ToFloat(stack.Pop())
                            ));

                            if (opcode == OpCode.SPVFS)
                                state.Freedom = vec;
                            else {
                                state.Projection = vec;
                                state.DualProjection = vec;
                            }
                        }
                        break;
                    case OpCode.GPV:
                    case OpCode.GFV:
                        {
                            var vec = opcode == OpCode.GPV ? state.Projection : state.Freedom;
                            stack.Push(FloatToF2Dot14(vec.X));
                            stack.Push(FloatToF2Dot14(vec.Y));
                        }
                        break;

                    // ==== GRAPHICS STATE ====
                    case OpCode.SRP0: state.Rp0 = stack.Pop(); break;
                    case OpCode.SRP1: state.Rp1 = stack.Pop(); break;
                    case OpCode.SRP2: state.Rp2 = stack.Pop(); break;
                    case OpCode.SZP0: state.Gep0 = GetZoneIndex(); break;
                    case OpCode.SZP1: state.Gep1 = GetZoneIndex(); break;
                    case OpCode.SZP2: state.Gep2 = GetZoneIndex(); break;
                    case OpCode.SZPS: SetAllZonePointers(); break;
                    case OpCode.SLOOP: state.Loop = stack.Pop(); break;
                    case OpCode.SMD: state.MinDistance = stack.Pop(); break;
                    case OpCode.SSWCI: state.SingleWidthCutIn = stack.Pop(); break;
                    case OpCode.SSW: state.SingleWidthValue = stack.Pop(); break;
                    case OpCode.FLIPON: state.AutoFlip = true; break;
                    case OpCode.FLIPOFF: state.AutoFlip = false; break;
                    case OpCode.SANGW: /* instruction unspported */ break;
                    case OpCode.SDB: state.DeltaBase = stack.Pop(); break;
                    case OpCode.SDS: state.DeltaShift = stack.Pop(); break;

                    // ==== STACK MANAGEMENT ====
                    case OpCode.DUP: stack.Duplicate(); break;
                    case OpCode.POP: stack.Pop(); break;
                    case OpCode.CLEAR: stack.Clear(); break;
                    case OpCode.SWAP: stack.Swap(); break;
                    case OpCode.DEPTH: stack.Depth(); break;
                    case OpCode.CINDEX: stack.Copy(); break;
                    case OpCode.MINDEX: stack.Move(); break;
                    case OpCode.ROLL: stack.Roll(); break;

                    // ==== FLOW CONTROL ====
                    case OpCode.IF: If(); break;
                    case OpCode.ELSE: Else(); break;
                    case OpCode.EIF: /* nothing to do */ break;
                    case OpCode.JROT: JumpRelative(true); break;
                    case OpCode.JROF: JumpRelative(false); break;
                    case OpCode.JMPR: Jump(stack.Pop() - 1); break;

                    // ==== LOGICAL OPS ====
                    case OpCode.LT: LessThan(); break;
                    case OpCode.LTEQ: LessThanOrEqual(); break;
                    case OpCode.GT: GreaterThan(); break;
                    case OpCode.GTEQ: GreaterThanOrEqual(); break;
                    case OpCode.EQ: Equal(); break;
                    case OpCode.NEQ: NotEqual(); break;
                    case OpCode.AND: And(); break;
                    case OpCode.OR: Or(); break;
                    case OpCode.NOT: stack.Push(!stack.PopBool()); break;

                    // ==== ARITHMETIC ====
                    case OpCode.ADD: Add(); break;
                    case OpCode.SUB: Subtract(); break;
                    case OpCode.DIV: Divide(); break;
                    case OpCode.MUL: Multiply(); break;
                    case OpCode.ABS: stack.Push(Math.Abs(stack.Pop())); break;
                    case OpCode.NEG: stack.Push(-stack.Pop()); break;
                    case OpCode.FLOOR: Floor(); break;
                    case OpCode.CEILING: Ceiling(); break;
                    case OpCode.MAX: stack.Push(Math.Max(stack.Pop(), stack.Pop())); break;
                    case OpCode.MIN: stack.Push(Math.Min(stack.Pop(), stack.Pop())); break;

                    // ==== FUNCTIONS ====
                    case OpCode.FDEF: DefineFunction(inFunction); break;
                    case OpCode.ENDF: Return(inFunction); return;
                    case OpCode.CALL: Call(); break;
                    case OpCode.LOOPCALL: LoopCall(); break;

                    // ==== POINT MEASUREMENT ====
                    case OpCode.GC0:
                    case OpCode.GC1: GetCoordinate(opcode - OpCode.GC0); break;
                    case OpCode.MPS: // MPS should return point size, but we assume DPI so it's the same as pixel size
                    case OpCode.MPPEM: stack.Push(ppem); break;

                    default:
                        throw new InvalidFontException("Unknown opcode in font program.");
                }
            }
        }

        // ==== instruction stream management ====
        int NextByte () {
            if (ip >= instructions.Length)
                throw new InvalidFontException();
            return instructions[ip++];
        }

        void SeekEither (OpCode a, OpCode b) {
            while (true) {
                var opcode = NextOpCode();
                if (opcode == a || opcode == b)
                    return;
            }
        }

        OpCode NextOpCode () => (OpCode)NextByte();
        int NextWord () => NextByte() << 8 | NextByte();
        void Seek (OpCode o) => SeekEither(o, o);
        void Jump (int offset) => ip += offset;

        // ==== PUSH instructions ====
        void PushBytes (int count) {
            for (int i = 0; i < count; i++)
                stack.Push(NextByte());
        }

        void PushWords (int count) {
            for (int i = 0; i < count; i++)
                stack.Push(NextWord());
        }

        void CheckIndex (int index, int length) {
            if (index < 0 || index >= length)
                throw new InvalidFontException();
        }

        // ==== State vector management ====

        void SetFreedomVectorToAxis (int axis) => state.Freedom = axis == 0 ? Vector2.UnitX : Vector2.UnitY;

        void SetProjectionVectorToAxis (int axis) {
            state.Projection = axis == 0 ? Vector2.UnitX : Vector2.UnitY;
            state.DualProjection = state.Projection;
        }

        void SetVectorToLine (int mode, bool dual) {
            // mode here should be as follows:
            // 0: SPVTL0
            // 1: SPVTL1
            // 2: SFVTL0
            // 3: SFVTL1
            var index1 = stack.Pop();
            var index2 = stack.Pop();
            var p1 = zp2.GetCurrent(index1);
            var p2 = zp1.GetCurrent(index2);

            var line = p2 - p1;
            if (line.LengthSquared() == 0) {
                // invalid; just set to whatever
                if (mode >= 2)
                    state.Freedom = Vector2.UnitX;
                else {
                    state.Projection = Vector2.UnitX;
                    state.DualProjection = Vector2.UnitX;
                }
            }
            else {
                // if mode is 1 or 3, we want a perpendicular vector
                if ((mode & 0x1) != 0)
                    line = new Vector2(-line.Y, line.X);

                line = Vector2.Normalize(line);

                if (mode >= 2)
                    state.Freedom = line;
                else {
                    state.Projection = line;
                    state.DualProjection = line;
                }
            }

            // set the dual projection vector using original points
            if (dual) {
                p1 = zp2.GetOriginal(index1);
                p2 = zp2.GetOriginal(index2);
                line = p2 - p1;

                if (line.LengthSquared() == 0)
                    state.DualProjection = Vector2.UnitX;
                else {
                    if ((mode & 0x1) != 0)
                        line = new Vector2(-line.Y, line.X);

                    state.DualProjection = Vector2.Normalize(line);
                }
            }
        }

        // ==== Graphics state management ====
        int GetZoneIndex () {
            var zone = stack.Pop();
            if (zone < 0 || zone > 1)
                throw new InvalidFontException();
            return zone;
        }

        void SetAllZonePointers () {
            var zone = GetZoneIndex();
            state.Gep0 = zone;
            state.Gep1 = zone;
            state.Gep2 = zone;
        }

        // ==== Flow Control ====
        void If () {
            // value is false; jump to the next else block or endif marker
            // otherwise, we don't have to do anything; we'll keep executing this block
            if (stack.PopBool())
                SeekEither(OpCode.ELSE, OpCode.EIF);
        }

        void Else () {
            // assume we hit the true statement of some previous if block
            // if we had hit false, we would have jumped over this
            Seek(OpCode.EIF);
        }

        void JumpRelative (bool comparand) {
            if (stack.PopBool() == comparand)
                Jump(stack.Pop() - 1);
            else
                stack.Pop();    // ignore the offset
        }

        // ==== Logical Operations ====
        void LessThan () {
            var b = (uint)stack.Pop();
            var a = (uint)stack.Pop();
            stack.Push(a < b);
        }

        void LessThanOrEqual () {
            var b = (uint)stack.Pop();
            var a = (uint)stack.Pop();
            stack.Push(a <= b);
        }

        void GreaterThan () {
            var b = (uint)stack.Pop();
            var a = (uint)stack.Pop();
            stack.Push(a > b);
        }

        void GreaterThanOrEqual () {
            var b = (uint)stack.Pop();
            var a = (uint)stack.Pop();
            stack.Push(a >= b);
        }

        void Equal () {
            var b = (uint)stack.Pop();
            var a = (uint)stack.Pop();
            stack.Push(a == b);
        }

        void NotEqual () {
            var b = (uint)stack.Pop();
            var a = (uint)stack.Pop();
            stack.Push(a != b);
        }

        void And () {
            var b = stack.PopBool();
            var a = stack.PopBool();
            stack.Push(a && b);
        }

        void Or () {
            var b = stack.PopBool();
            var a = stack.PopBool();
            stack.Push(a || b);
        }

        // ==== Arithmetic ====
        void Add () {
            var b = stack.Pop();
            var a = stack.Pop();
            stack.Push(a + b);
        }

        void Subtract () {
            var b = stack.Pop();
            var a = stack.Pop();
            stack.Push(a - b);
        }

        void Divide () {
            var b = stack.Pop();
            if (b == 0)
                throw new InvalidFontException("Division by zero.");

            var a = stack.Pop();
            var result = ((long)a << 6) / b;
            stack.Push((int)result);
        }

        void Multiply () {
            var b = stack.Pop();
            var a = stack.Pop();
            var result = ((long)a * b) >> 6;
            stack.Push((int)result);
        }

        void Floor () {
            stack.Push(stack.Pop() & ~63);
        }

        void Ceiling () {
            stack.Push((stack.Pop() + 63) & ~63);
        }

        // ==== Function Calls ====
        void DefineFunction (bool inFunction) {
            if (inFunction)
                throw new InvalidFontException("Can't define a function inside another function.");

            functions[stack.Pop()] = new FunctionDef(instructions, ip);
            Seek(OpCode.ENDF);
        }

        void Return (bool inFunction) {
            if (!inFunction)
                throw new InvalidFontException("Found invalid ENDF marker outside of a function definition.");

            // nothing to do here; our caller will break the loop and return to its parent
        }

        void Call () {
            if (callStackSize > MaxCallStack)
                throw new InvalidFontException("Stack overflow; infinite recursion?");

            var function = functions[stack.Pop()];
            var currentIp = ip;
            var currentInstructions = instructions;

            callStackSize++;
            Execute(function.Instructions, function.Offset, true);
            callStackSize--;

            // restore our instruction stream
            ip = currentIp;
            instructions = currentInstructions;
        }

        void LoopCall () {
            if (callStackSize > MaxCallStack)
                throw new InvalidFontException("Stack overflow; infinite recursion?");

            var function = functions[stack.Pop()];
            var currentIp = ip;
            var currentInstructions = instructions;

            callStackSize++;

            var count = stack.Pop();
            for (int i = 0; i < count; i++)
                Execute(function.Instructions, function.Offset, true);

            callStackSize--;

            // restore our instruction stream
            ip = currentIp;
            instructions = currentInstructions;
        }

        // ==== Point Measurement ====
        void GetCoordinate (int mode) {
        }

        static void DebugPrint (OpCode opcode) {
            switch (opcode) {
                case OpCode.FDEF:
                case OpCode.PUSHB1:
                case OpCode.PUSHB2:
                case OpCode.PUSHB3:
                case OpCode.PUSHB4:
                case OpCode.PUSHB5:
                case OpCode.PUSHB6:
                case OpCode.PUSHB7:
                case OpCode.PUSHB8:
                case OpCode.PUSHW1:
                case OpCode.PUSHW2:
                case OpCode.PUSHW3:
                case OpCode.PUSHW4:
                case OpCode.PUSHW5:
                case OpCode.PUSHW6:
                case OpCode.PUSHW7:
                case OpCode.PUSHW8:
                case OpCode.NPUSHB:
                case OpCode.NPUSHW:
                    return;
            }

            Debug.WriteLine(opcode);
        }

        static float F2Dot14ToFloat (int value) => (short)value / 16384.0f;
        static int FloatToF2Dot14 (float value) => (int)(uint)(short)Math.Round(value * 16384.0f);

        static float F26Dot6ToFloat (int value) => value / 64.0f;
        static int FloatToF26Dot6 (float value) => (int)Math.Round(value * 64.0f);

        const int MaxCallStack = 128;

        struct Zone {
            public Vector2 GetCurrent (int index) {
                return Vector2.Zero;
            }

            public Vector2 GetOriginal (int index) {
                return Vector2.Zero;
            }
        }

        enum OpCode : byte {
            SVTCA0,
            SVTCA1,
            SPVTCA0,
            SPVTCA1,
            SFVTCA0,
            SFVTCA1,
            SPVTL0,
            SPVTL1,
            SFVTL0,
            SFVTL1,
            SPVFS,
            SFVFS,
            GPV,
            GFV,
            SFVTPV,
            SRP0 = 0x10,
            SRP1,
            SRP2,
            SZP0,
            SZP1,
            SZP2,
            SZPS,
            SLOOP,
            SMD = 0x1A,
            ELSE,
            JMPR,
            SSWCI = 0x1E,
            SSW,
            DUP = 0x20,
            POP,
            CLEAR,
            SWAP,
            DEPTH,
            CINDEX,
            MINDEX,
            LOOPCALL = 0x2A,
            CALL,
            FDEF,
            ENDF,
            NPUSHB = 0x40,
            NPUSHW,
            WS = 0x42,
            RS,
            WCVTP,
            RCVT,
            GC0,
            GC1,
            SCFS,
            MPPEM = 0x4B,
            MPS,
            FLIPON = 0x4D,
            FLIPOFF,
            LT = 0x50,
            LTEQ,
            GT,
            GTEQ,
            EQ,
            NEQ,
            IF = 0x58,
            EIF,
            AND = 0x5A,
            OR,
            NOT,
            SDB = 0x5E,
            SDS,
            ADD = 0x60,
            SUB,
            DIV,
            MUL,
            ABS,
            NEG,
            FLOOR,
            CEILING,
            WCVTF = 0x70,
            JROT = 0x78,
            JROF,
            SANGW = 0x7E,
            SDPVTL0 = 0x86,
            SDPVTL1,
            ROLL = 0x8A,
            MAX,
            MIN,
            PUSHB1 = 0xB0,
            PUSHB2,
            PUSHB3,
            PUSHB4,
            PUSHB5,
            PUSHB6,
            PUSHB7,
            PUSHB8,
            PUSHW1 = 0xB8,
            PUSHW2,
            PUSHW3,
            PUSHW4,
            PUSHW5,
            PUSHW6,
            PUSHW7,
            PUSHW8
        }
    }
}
