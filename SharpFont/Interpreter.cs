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
        public float ControlValueCutIn = 17.0f / 16.0f;
        public int DeltaBase = 9;
        public int DeltaShift = 3;
        public Vector2 DualProjection = Vector2.UnitX;
        public Vector2 Freedom = Vector2.UnitX;
        public ZoneType Gep0 = ZoneType.Points;
        public ZoneType Gep1 = ZoneType.Points;
        public ZoneType Gep2 = ZoneType.Points;
        public InstructionControlFlags InstructionControl;
        public int Loop = 1;
        public float MinDistance = 1.0f;
        public Vector2 Projection = Vector2.UnitX;
        public RoundMode RoundState = RoundMode.ToGrid;
        public int Rp0;
        public int Rp1;
        public int Rp2;
        public float SingleWidthCutIn;
        public float SingleWidthValue;
    }

    enum ZoneType {
        Twilight,
        Points
    }

    enum RoundMode {
        ToHalfGrid,
        ToGrid,
        ToDoubleGrid,
        DownToGrid,
        UpToGrid,
        Off,
        Super,
        Super45
    }

    [Flags]
    enum InstructionControlFlags {
        None,
        InhibitGridFitting = 0x1,
        UseDefaultGraphicsState = 0x2
    }

    class ExecutionStack {
        int[] s;
        int count;

        public ExecutionStack (int maxStack) {
            s = new int[maxStack];
        }

        public int Peek () => Peek(0);
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
            for (int i = count - index - 1; i < count - 1; i++)
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
            return s[count - index - 1];
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
        float fdotp;
        float roundThreshold;
        float roundPhase;
        float roundPeriod;
        Zone zp0, zp1, zp2;
        Zone points, twilight;

        public Interpreter (int maxStack, int maxStorage, int maxFunctions, int maxTwilightPoints) {
            stack = new ExecutionStack(maxStack);
            storage = new int[maxStorage];
            functions = new FunctionDef[maxFunctions];
            state = new GraphicsState();

            twilight = new Zone(new PointF[maxTwilightPoints], isTwilight: true);
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

        void Execute (byte[] instr, int offset, bool inFunction) {
            instructions = instr;
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
                    case OpCode.RCVT: stack.Push(FloatToF26Dot6(ReadCvt())); break;

                    // ==== STATE VECTORS ====
                    case OpCode.SVTCA0:
                    case OpCode.SVTCA1:
                        {
                            var axis = opcode - OpCode.SVTCA0;
                            SetFreedomVectorToAxis(axis);
                            SetProjectionVectorToAxis(axis);
                        }
                        break;
                    case OpCode.SFVTPV: state.Freedom = state.Projection; OnVectorsUpdated(); break;
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

                            OnVectorsUpdated();
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
                    case OpCode.SZP0: state.Gep0 = GetZoneFromStack(out zp0); break;
                    case OpCode.SZP1: state.Gep1 = GetZoneFromStack(out zp1); break;
                    case OpCode.SZP2: state.Gep2 = GetZoneFromStack(out zp2); break;
                    case OpCode.SZPS:
                        {
                            Zone zone;
                            var index = GetZoneFromStack(out zone);
                            state.Gep0 = index; zp0 = zone;
                            state.Gep1 = index; zp1 = zone;
                            state.Gep2 = index; zp2 = zone;
                        }
                        break;
                    case OpCode.RTHG: state.RoundState = RoundMode.ToHalfGrid; break;
                    case OpCode.RTG: state.RoundState = RoundMode.ToGrid; break;
                    case OpCode.RTDG: state.RoundState = RoundMode.ToDoubleGrid; break;
                    case OpCode.RDTG: state.RoundState = RoundMode.DownToGrid; break;
                    case OpCode.RUTG: state.RoundState = RoundMode.UpToGrid; break;
                    case OpCode.ROFF: state.RoundState = RoundMode.Off; break;
                    case OpCode.SROUND:
                        {
                            state.RoundState = RoundMode.Super;
                            SetSuperRound(1.0f, stack.Pop());
                        }
                        break;
                    case OpCode.S45ROUND:
                        {
                            state.RoundState = RoundMode.Super45;
                            SetSuperRound(Sqrt2Over2, stack.Pop());
                        }
                        break;
                    case OpCode.INSTCTRL:
                        {
                            var selector = stack.Pop();
                            var value = stack.Pop();
                            if (selector >= 1 && selector <= 2) {
                                // value is false if zero, otherwise shift the right bit into the flags
                                var bit = 1 << (selector - 1);
                                if (value == 0)
                                    state.InstructionControl = (InstructionControlFlags)((int)state.InstructionControl & ~bit);
                                else
                                    state.InstructionControl = (InstructionControlFlags)((int)state.InstructionControl | bit);
                            }
                        }
                        break;
                    case OpCode.SCANCTRL: /* instruction unspported */ stack.Pop(); break;
                    case OpCode.SCANTYPE: /* instruction unspported */ stack.Pop(); break;
                    case OpCode.SANGW: /* instruction unspported */ stack.Pop(); break;
                    case OpCode.SLOOP: state.Loop = stack.Pop(); break;
                    case OpCode.SMD: state.MinDistance = F26Dot6ToFloat(stack.Pop()); break;
                    case OpCode.SCVTCI: state.ControlValueCutIn = F26Dot6ToFloat(stack.Pop()); break;
                    case OpCode.SSWCI: state.SingleWidthCutIn = F26Dot6ToFloat(stack.Pop()); break;
                    case OpCode.SSW: state.SingleWidthValue = stack.Pop() * scale; break;
                    case OpCode.FLIPON: state.AutoFlip = true; break;
                    case OpCode.FLIPOFF: state.AutoFlip = false; break;
                    case OpCode.SDB: state.DeltaBase = stack.Pop(); break;
                    case OpCode.SDS: state.DeltaShift = stack.Pop(); break;

                    // ==== POINT MEASUREMENT ====
                    case OpCode.GC0:
                        {
                            var point = zp2.GetCurrent(stack.Pop());
                            stack.Push(FloatToF26Dot6(Vector2.Dot(point, state.Projection)));
                        }
                        break;
                    case OpCode.GC1:
                        {
                            var point = zp2.GetOriginal(stack.Pop());
                            stack.Push(FloatToF26Dot6(Vector2.Dot(point, state.DualProjection)));
                        }
                        break;
                    case OpCode.SCFS:
                        {
                            var value = F26Dot6ToFloat(stack.Pop());
                            var index = stack.Pop();
                            var point = zp2.GetCurrent(index);
                            var projection = Vector2.Dot(point, state.DualProjection);
                            point = MovePoint(point, value - projection);
                            zp2.SetCurrent(index, point);

                            // moving twilight points moves their "original" value also
                            if (state.Gep2 == ZoneType.Twilight)
                                zp2.SetOriginal(index, point);
                        }
                        break;
                    case OpCode.MD0:
                        {
                            var p1 = zp1.GetCurrent(stack.Pop());
                            var p2 = zp0.GetCurrent(stack.Pop());
                            var distance = Vector2.Dot(p2 - p1, state.Projection);
                            stack.Push(FloatToF26Dot6(distance));
                        }
                        break;
                    case OpCode.MD1:
                        {
                            var p1 = zp1.GetOriginal(stack.Pop());
                            var p2 = zp0.GetOriginal(stack.Pop());
                            var distance = Vector2.Dot(p2 - p1, state.DualProjection);
                            stack.Push(FloatToF26Dot6(distance));
                        }
                        break;
                    case OpCode.MPS: // MPS should return point size, but we assume DPI so it's the same as pixel size
                    case OpCode.MPPEM: stack.Push(ppem); break;

                    // ==== POINT MODIFICATION ====
                    case OpCode.FLIPPT:
                        {
                            for (int i = 0; i < state.Loop; i++)
                                points.Flip(stack.Pop());
                            state.Loop = 1;
                        }
                        break;
                    case OpCode.FLIPRGON:
                        {
                            var end = stack.Pop();
                            for (int i = stack.Pop(); i <= end; i++)
                                points.FlipOn(i);
                        }
                        break;
                    case OpCode.FLIPRGOFF:
                        {
                            var end = stack.Pop();
                            for (int i = stack.Pop(); i <= end; i++)
                                points.FlipOff(i);
                        }
                        break;
                    case OpCode.SHP0:
                    case OpCode.SHP1:
                        {
                            // compute displacement of the reference point
                            Zone refZone;
                            int refPoint;
                            if (opcode == OpCode.SHP0) {
                                refZone = zp1;
                                refPoint = state.Rp2;
                            }
                            else {
                                refZone = zp0;
                                refPoint = state.Rp1;
                            }

                            var distance = Vector2.Dot(refZone.GetCurrent(refPoint) - refZone.GetOriginal(refPoint), state.Projection);
                            ShiftPoints(MovePoint(state.Freedom, distance));
                        }
                        break;
                    case OpCode.SHPIX: ShiftPoints(stack.Pop() * state.Freedom); break;
                    case OpCode.MIAP0:
                    case OpCode.MIAP1:
                        {
                            var distance = ReadCvt();
                            var pointIndex = stack.Pop();

                            // this instruction is used in the CVT to set up twilight points with original values
                            if (state.Gep0 == ZoneType.Twilight) {
                                var original = state.Freedom * distance;
                                zp0.SetOriginal(pointIndex, original);
                                zp0.SetCurrent(pointIndex, original);
                            }

                            // current position of the point along the projection vector
                            var point = zp0.GetCurrent(pointIndex);
                            var currentPos = Vector2.Dot(point, state.Projection);
                            if (opcode == OpCode.MIAP1) {
                                // only use the CVT if we are above the cut-in point
                                if (Math.Abs(distance - currentPos) > state.ControlValueCutIn)
                                    distance = currentPos;

                                distance = Round(distance);
                            }

                            zp0.SetCurrent(pointIndex, MovePoint(point, distance - currentPos));
                            state.Rp0 = pointIndex;
                            state.Rp1 = pointIndex;
                        }
                        break;
                    case OpCode.IP:
                        {
                            var originalBase = zp0.GetOriginal(state.Rp1);
                            var currentBase = zp0.GetCurrent(state.Rp1);
                            var originalRange = Vector2.Dot(zp1.GetOriginal(state.Rp2) - originalBase, state.DualProjection);
                            var currentRange = Vector2.Dot(zp1.GetCurrent(state.Rp2) - currentBase, state.Projection);

                            for (int i = 0; i < state.Loop; i++) {
                                var pointIndex = stack.Pop();
                                var point = zp2.GetCurrent(pointIndex);
                                var currentDistance = Vector2.Dot(point - currentBase, state.Projection);
                                var originalDistance = Vector2.Dot(zp2.GetOriginal(pointIndex) - originalBase, state.DualProjection);

                                var newDistance = 0.0f;
                                if (originalDistance != 0.0f) {
                                    // a range of 0.0f is invalid according to the spec (would result in a div by zero)
                                    if (originalRange == 0.0f)
                                        newDistance = originalDistance;
                                    else
                                        newDistance = originalDistance * currentRange / originalRange;
                                }

                                zp2.SetCurrent(pointIndex, MovePoint(point, newDistance - currentDistance));
                            }

                            state.Loop = 1;
                        }
                        break;

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
                    case OpCode.IF:
                        {
                            // value is false; jump to the next else block or endif marker
                            // otherwise, we don't have to do anything; we'll keep executing this block
                            if (!stack.PopBool())
                                SeekEither(OpCode.ELSE, OpCode.EIF);
                        }
                        break;
                    case OpCode.ELSE:
                        {
                            // assume we hit the true statement of some previous if block
                            // if we had hit false, we would have jumped over this
                            Seek(OpCode.EIF);
                        }
                        break;
                    case OpCode.EIF: /* nothing to do */ break;
                    case OpCode.JROT: JumpRelative(true); break;
                    case OpCode.JROF: JumpRelative(false); break;
                    case OpCode.JMPR: Jump(stack.Pop() - 1); break;

                    // ==== LOGICAL OPS ====
                    case OpCode.LT:
                        {
                            var b = (uint)stack.Pop();
                            var a = (uint)stack.Pop();
                            stack.Push(a < b);
                        }
                        break;
                    case OpCode.LTEQ:
                        {
                            var b = (uint)stack.Pop();
                            var a = (uint)stack.Pop();
                            stack.Push(a <= b);
                        }
                        break;
                    case OpCode.GT:
                        {
                            var b = (uint)stack.Pop();
                            var a = (uint)stack.Pop();
                            stack.Push(a > b);
                        }
                        break;
                    case OpCode.GTEQ:
                        {
                            var b = (uint)stack.Pop();
                            var a = (uint)stack.Pop();
                            stack.Push(a >= b);
                        }
                        break;
                    case OpCode.EQ:
                        {
                            var b = (uint)stack.Pop();
                            var a = (uint)stack.Pop();
                            stack.Push(a == b);
                        }
                        break;
                    case OpCode.NEQ:
                        {
                            var b = (uint)stack.Pop();
                            var a = (uint)stack.Pop();
                            stack.Push(a != b);
                        }
                        break;
                    case OpCode.AND:
                        {
                            var b = stack.PopBool();
                            var a = stack.PopBool();
                            stack.Push(a && b);
                        }
                        break;
                    case OpCode.OR:
                        {
                            var b = stack.PopBool();
                            var a = stack.PopBool();
                            stack.Push(a || b);
                        }
                        break;
                    case OpCode.NOT: stack.Push(!stack.PopBool()); break;
                    case OpCode.ODD:
                        {
                            var value = (int)Round(F26Dot6ToFloat(stack.Pop()));
                            stack.Push(value % 2 != 0);
                        }
                        break;
                    case OpCode.EVEN:
                        {
                            var value = (int)Round(F26Dot6ToFloat(stack.Pop()));
                            stack.Push(value % 2 == 0);
                        }
                        break;

                    // ==== ARITHMETIC ====
                    case OpCode.ADD:
                        {
                            var b = stack.Pop();
                            var a = stack.Pop();
                            stack.Push(a + b);
                        }
                        break;
                    case OpCode.SUB:
                        {
                            var b = stack.Pop();
                            var a = stack.Pop();
                            stack.Push(a - b);
                        }
                        break;
                    case OpCode.DIV:
                        {
                            var b = stack.Pop();
                            if (b == 0)
                                throw new InvalidFontException("Division by zero.");

                            var a = stack.Pop();
                            var result = ((long)a << 6) / b;
                            stack.Push((int)result);
                        }
                        break;
                    case OpCode.MUL:
                        {
                            var b = stack.Pop();
                            var a = stack.Pop();
                            var result = ((long)a * b) >> 6;
                            stack.Push((int)result);
                        }
                        break;
                    case OpCode.ABS: stack.Push(Math.Abs(stack.Pop())); break;
                    case OpCode.NEG: stack.Push(-stack.Pop()); break;
                    case OpCode.FLOOR: stack.Push(stack.Pop() & ~63); break;
                    case OpCode.CEILING: stack.Push((stack.Pop() + 63) & ~63); break;
                    case OpCode.MAX: stack.Push(Math.Max(stack.Pop(), stack.Pop())); break;
                    case OpCode.MIN: stack.Push(Math.Min(stack.Pop(), stack.Pop())); break;

                    // ==== FUNCTIONS ====
                    case OpCode.FDEF:
                        {
                            if (inFunction)
                                throw new InvalidFontException("Can't define a function inside another function.");

                            functions[stack.Pop()] = new FunctionDef(instructions, ip);
                            Seek(OpCode.ENDF);
                        }
                        break;
                    case OpCode.ENDF:
                        {
                            if (!inFunction)
                                throw new InvalidFontException("Found invalid ENDF marker outside of a function definition.");
                            return;     // return to caller
                        }
                    case OpCode.CALL:
                        {
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
                        break;
                    case OpCode.LOOPCALL:
                        {
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
                        break;

                    // ==== ROUNDING ====
                    // we don't have "engine compensation" so the variants are unnecessary
                    case OpCode.ROUND0:
                    case OpCode.ROUND1:
                    case OpCode.ROUND2:
                    case OpCode.ROUND3:
                        {
                            var value = F26Dot6ToFloat(stack.Pop());
                            value = Round(value);
                            stack.Push(FloatToF26Dot6(value));
                        }
                        break;
                    case OpCode.NROUND0:
                    case OpCode.NROUND1:
                    case OpCode.NROUND2:
                    case OpCode.NROUND3: break;

                    // ==== MISCELLANEOUS ====
                    case OpCode.DEBUG: stack.Pop(); break;
                    case OpCode.GETINFO:
                        {
                            var selector = stack.Pop();
                            var result = 0;
                            if ((selector & 0x1) != 0) {
                                // pretend we are MS Rasterizer v35
                                result = 35;
                            }

                            // TODO: rotation and stretching
                            //if ((selector & 0x2) != 0)
                            //if ((selector & 0x4) != 0)

                            // we're always rendering in grayscale
                            if ((selector & 0x20) != 0)
                                result |= 1 << 12;

                            // TODO: ClearType flags

                            stack.Push(result);
                        }
                        break;

                    default:
                        if (opcode >= OpCode.MIRP)
                            MoveIndirectRelative(opcode - OpCode.MIRP);
                        else
                            throw new InvalidFontException("Unknown opcode in font program.");
                        break;
                }
            }
        }

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

        float ReadCvt () {
            var loc = stack.Pop();
            CheckIndex(loc, controlValueTable.Length);
            return controlValueTable[loc];
        }

        void OnVectorsUpdated () {
            fdotp = Vector2.Dot(state.Freedom, state.Projection);
            if (fdotp < Epsilon)
                fdotp = 1.0f;
        }

        void SetFreedomVectorToAxis (int axis) {
            state.Freedom = axis == 0 ? Vector2.UnitY : Vector2.UnitX;
            OnVectorsUpdated();
        }

        void SetProjectionVectorToAxis (int axis) {
            state.Projection = axis == 0 ? Vector2.UnitY : Vector2.UnitX;
            state.DualProjection = state.Projection;

            OnVectorsUpdated();
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

            OnVectorsUpdated();
        }

        ZoneType GetZoneFromStack (out Zone zone) {
            var index = stack.Pop();
            switch (index) {
                case 0: zone = twilight; break;
                case 1: zone = points; break;
                default: throw new InvalidFontException("Invalid zone pointer.");
            }
            return (ZoneType)index;
        }

        void SetSuperRound (float period, int mode) {
            // mode is a bunch of packed flags
            // bits 7-6 are the period multiplier
            switch (mode & 0xC0) {
                case 0: roundPeriod = period / 2; break;
                case 0x40: roundPeriod = period; break;
                case 0x80: roundPeriod = period * 2; break;
                default: throw new InvalidFontException("Unknown rounding period multiplier.");
            }

            // bits 5-4 are the phase
            switch (mode & 0x30) {
                case 0: roundPhase = 0; break;
                case 0x10: roundPhase = roundPeriod / 4; break;
                case 0x20: roundPhase = roundPeriod / 2; break;
                case 0x30: roundPhase = roundPeriod * 3 / 4; break;
            }

            // bits 3-0 are the threshold
            if ((mode & 0xF) == 0)
                roundThreshold = roundPeriod - 1;
            else
                roundThreshold = ((mode & 0xF) - 4) * roundPeriod / 8;
        }

        void JumpRelative (bool comparand) {
            if (stack.PopBool() == comparand)
                Jump(stack.Pop() - 1);
            else
                stack.Pop();    // ignore the offset
        }

        void MoveIndirectRelative (int flags) {
            // this instruction tries to make the current distance between a given point
            // and the reference point rp0 be equivalent to the same distance in the original outline
            // there are a bunch of flags that control how that distance is measured
            var cvt = ReadCvt();
            var pointIndex = stack.Pop();

            if (Math.Abs(cvt - state.SingleWidthValue) < state.SingleWidthCutIn) {
                if (cvt >= 0)
                    cvt = state.SingleWidthValue;
                else
                    cvt = -state.SingleWidthValue;
            }

            // if we're looking at the twilight zone we need to prepare the points there
            var originalReference = zp0.GetOriginal(state.Rp0);
            if (state.Gep1 == ZoneType.Twilight) {
                var initialValue = originalReference + state.Freedom * cvt;
                zp1.SetOriginal(pointIndex, initialValue);
                zp1.SetCurrent(pointIndex, initialValue);
            }

            var point = zp1.GetCurrent(pointIndex);
            var originalDistance = Vector2.Dot(zp1.GetOriginal(pointIndex) - originalReference, state.DualProjection);
            var currentDistance = Vector2.Dot(point - zp0.GetCurrent(state.Rp0), state.Projection);

            if (state.AutoFlip && Math.Sign(originalDistance) != Math.Sign(cvt))
                cvt = -cvt;

            // if bit 2 is set, round the distance and look at the cut-in value
            var distance = cvt;
            if ((flags & 0x4) != 0) {
                // only perform cut-in tests when both points are in the same zone
                if (state.Gep0 == state.Gep1 && Math.Abs(cvt - originalDistance) > state.ControlValueCutIn)
                    cvt = originalDistance;

                distance = Round(cvt);
            }

            // if bit 3 is set, constrain to the minimum distance
            if ((flags & 0x8) != 0) {
                if (originalDistance >= 0)
                    distance = Math.Max(distance, state.MinDistance);
                else
                    distance = Math.Min(distance, -state.MinDistance);
            }

            zp1.SetCurrent(pointIndex, MovePoint(point, distance - currentDistance));

            state.Rp1 = state.Rp0;
            state.Rp2 = pointIndex;
            if ((flags & 0x10) != 0)
                state.Rp0 = pointIndex;
        }

        void ShiftPoints (Vector2 displacement) {
            for (int i = 0; i < state.Loop; i++) {
                var pointIndex = stack.Pop();
                var point = zp2.GetCurrent(pointIndex);
                zp2.SetCurrent(pointIndex, point + displacement);
            }
            state.Loop = 1;
        }

        float Round (float value) {
            switch (state.RoundState) {
                case RoundMode.ToGrid: return (float)Math.Round(value);
                case RoundMode.ToHalfGrid: return (float)Math.Floor(value) + Math.Sign(value) * 0.5f;
                case RoundMode.ToDoubleGrid: return (float)(Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2);
                case RoundMode.DownToGrid: return (float)Math.Floor(value);
                case RoundMode.UpToGrid: return (float)Math.Ceiling(value);
                case RoundMode.Super:
                case RoundMode.Super45:
                    var sign = Math.Sign(value);
                    value = value - roundPhase + roundThreshold;
                    value = (float)Math.Truncate(value / roundPeriod) * roundPeriod;
                    value += roundPhase;
                    if (sign < 0 && value > 0)
                        value = -roundPhase;
                    else if (sign >= 0 && value < 0)
                        value = roundPhase;
                    return value;

                default: return value;
            }
        }

        List<OpCode> debugList = new List<OpCode>();
        void DebugPrint (OpCode opcode) {
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

            debugList.Add(opcode);
            //Debug.WriteLine(opcode);
        }

        Vector2 MovePoint (Vector2 point, float distance) => point + distance * state.Freedom / fdotp;

        static float F2Dot14ToFloat (int value) => (short)value / 16384.0f;
        static int FloatToF2Dot14 (float value) => (int)(uint)(short)Math.Round(value * 16384.0f);

        static float F26Dot6ToFloat (int value) => value / 64.0f;
        static int FloatToF26Dot6 (float value) => (int)Math.Round(value * 64.0f);

        static readonly float Sqrt2Over2 = (float)(Math.Sqrt(2) / 2);

        const int MaxCallStack = 128;
        const float Epsilon = 0.000001f;

        struct Zone {
            PointF[] current;
            PointF[] original;
            bool isTwilight;

            public Zone (PointF[] points, bool isTwilight) {
                this.isTwilight = isTwilight;

                original = points;
                current = (PointF[])points.Clone();
            }

            public Vector2 GetCurrent (int index) => current[index].P;
            public Vector2 GetOriginal (int index) => original[index].P;

            public void SetCurrent (int index, Vector2 value) => current[index].P = value;

            public void SetOriginal (int index, Vector2 value) {
                if (!isTwilight)
                    throw new InvalidOperationException("Can't modify original points that are not in the twilight zone.");
                original[index].P = value;
            }

            public void Flip (int index) {
            }

            public void FlipOn (int index) {
            }

            public void FlipOff (int index) {
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
            RTG,
            RTHG,
            SMD = 0x1A,
            ELSE,
            JMPR,
            SCVTCI,
            SSWCI,
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
            RTDG,
            SHP0 = 0x32,
            SHP1,
            SHC0,
            SHC1,
            SHZ0,
            SHZ1,
            SHPIX,
            IP,
            MIAP0 = 0x3E,
            MIAP1,
            NPUSHB = 0x40,
            NPUSHW,
            WS = 0x42,
            RS,
            WCVTP,
            RCVT,
            GC0,
            GC1,
            SCFS,
            MD0,
            MD1,
            MPPEM,
            MPS,
            FLIPON,
            FLIPOFF,
            DEBUG,
            LT = 0x50,
            LTEQ,
            GT,
            GTEQ,
            EQ,
            NEQ,
            ODD,
            EVEN,
            IF,
            EIF,
            AND,
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
            ROUND0 = 0x68,
            ROUND1,
            ROUND2,
            ROUND3,
            NROUND0,
            NROUND1,
            NROUND2,
            NROUND3,
            WCVTF = 0x70,
            SROUND = 0x76,
            S45ROUND,
            JROT,
            JROF,
            ROFF = 0x7A,
            RUTG = 0x7C,
            RDTG,
            SANGW,
            FLIPPT = 0x80,
            FLIPRGON,
            FLIPRGOFF,
            SCANCTRL = 0x85,
            SDPVTL0,
            SDPVTL1,
            GETINFO,
            ROLL = 0x8A,
            MAX,
            MIN,
            SCANTYPE,
            INSTCTRL,
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
            PUSHW8,
            MIRP = 0xE0
        }
    }
}
