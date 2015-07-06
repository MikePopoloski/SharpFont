using System;
using System.Collections.Generic;
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

    class Interpreter {
        GraphicsState state;
        ExecutionStack stack;
        float[] cvt;
        byte[] instructions;
        int[] storage;
        int ip;
        float scale;

        public Interpreter (int maxStack, int maxStorage) {
            stack = new ExecutionStack(maxStack);
            storage = new int[maxStorage];
        }

        public void Execute (byte[] instructions, int offset) {
            this.instructions = instructions;
            ip = offset;

            // dispatch each instruction in the stream
            var length = instructions.Length;
            while (ip < length) {
                var opcode = NextOpCode();
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
                    case OpCode.RS: ReadStorage(); break;
                    case OpCode.WS: WriteStorage(); break;

                    // ==== CONTROL VALUE TABLE ====
                    case OpCode.WCVTP: WriteCvtP(); break;
                    case OpCode.WCVTF: WriteCvtF(); break;
                    case OpCode.RCVT: ReadCvt(); break;

                    // ==== STATE VECTORS ====
                    case OpCode.SVTCA0:
                    case OpCode.SVTCA1: SetVectorsToAxis(opcode - OpCode.SVTCA0); break;
                    case OpCode.SPVTCA0:
                    case OpCode.SPVTCA1: SetProjectionVectorToAxis(opcode - OpCode.SPVTCA0); break;
                    case OpCode.SFVTCA0:
                    case OpCode.SFVTCA1: SetFreedomVectorToAxis(opcode - OpCode.SFVTCA0); break;
                    case OpCode.SPVTL0:
                    case OpCode.SPVTL1: SetProjectionVectorToLine(opcode - OpCode.SPVTL0); break;
                    case OpCode.SFVTL0:
                    case OpCode.SFVTL1: SetFreedomVectorToLine(opcode - OpCode.SFVTL0); break;
                    case OpCode.SFVTPV: SetFreedomVectorToProjectionVector(); break;
                    case OpCode.SDPVTL0:
                    case OpCode.SDPVTL1: SetDualProjectionVectorToLine(opcode - OpCode.SDPVTL0); break;
                    case OpCode.SPVFS: SetProjectionVectorFromStack(); break;
                    case OpCode.SFVFS: SetFreedomVectorFromStack(); break;
                    case OpCode.GPV: GetProjectionVector(); break;
                    case OpCode.GFV: GetFreedomVector(); break;

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
                    case OpCode.EIF: throw new InvalidFontException();
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
                    //case OpCode.ADD: Add(); break;
                    //case OpCode.SUB: Subtract(); break;
                    //case OpCode.DIV: Divide(); break;
                    //case OpCode.MUL: Multiply(); break;
                    case OpCode.ABS: stack.Push(Math.Abs(stack.Pop())); break;
                    case OpCode.NEG: stack.Push(-stack.Pop()); break;
                    //case OpCode.FLOOR: Floor(); break;
                    //case OpCode.CEILING: Ceiling(); break;
                    case OpCode.MAX: stack.Push(Math.Max(stack.Pop(), stack.Pop())); break;
                    case OpCode.MIN: stack.Push(Math.Min(stack.Pop(), stack.Pop())); break;
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

        // ==== Storage management ====
        void ReadStorage () {
            var loc = stack.Pop();
            CheckIndex(loc, storage.Length);
            stack.Push(storage[loc]);
        }

        void WriteStorage () {
            var value = stack.Pop();
            var loc = stack.Pop();
            CheckIndex(loc, storage.Length);
            storage[loc] = value;
        }

        // ==== Control Value Table ====
        void WriteCvtP () {
            var value = stack.Pop();
            var loc = stack.Pop();
            CheckIndex(loc, cvt.Length);
            cvt[loc] = F26Dot6ToFloat(value);
        }

        void WriteCvtF () {
            var value = stack.Pop();
            var loc = stack.Pop();
            CheckIndex(loc, cvt.Length);
            cvt[loc] = value * scale;
        }

        void ReadCvt () {
            var loc = stack.Pop();
            CheckIndex(loc, cvt.Length);
            stack.Push(FloatToF26Dot6(cvt[loc]));
        }

        void CheckIndex (int index, int length) {
            if (index < 0 || index >= length)
                throw new InvalidFontException();
        }

        // ==== State vector management ====	
        void SetVectorsToAxis (int axis) {
            SetFreedomVectorToAxis(axis);
            SetProjectionVectorToAxis(axis);
        }

        void SetProjectionVectorToAxis (int axis) => state.Projection = axis == 0 ? Vector2.UnitX : Vector2.UnitY;
        void SetFreedomVectorToAxis (int axis) => state.Freedom = axis == 0 ? Vector2.UnitX : Vector2.UnitY;
        void SetFreedomVectorToProjectionVector () => state.Freedom = state.Projection;

        void SetProjectionVectorToLine (int mode) {
        }

        void SetFreedomVectorToLine (int mode) {
        }

        void SetDualProjectionVectorToLine (int mode) {
        }

        void SetProjectionVectorFromStack () {
        }

        void SetFreedomVectorFromStack () {
        }

        void GetProjectionVector () {
        }

        void GetFreedomVector () {
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

        float F26Dot6ToFloat (int value) => value / 64.0f;
        int FloatToF26Dot6 (float value) => (int)Math.Round(value * 64.0f);

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
            NPUSHB = 0x40,
            NPUSHW,
            WS = 0x42,
            RS,
            WCVTP,
            RCVT,
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
