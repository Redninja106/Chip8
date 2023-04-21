
using ImGuiNET;
using SimulationFramework;
using SimulationFramework.Desktop;
using SimulationFramework.Drawing;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

string file = null;
file = "Roms/Particle Demo [zeroZshadow, 2008].ch8";
// file = "Roms/Tic-Tac-Toe [David Winter].ch8";
// file = "Roms/Tetris [Fran Dachille, 1991].ch8";
// file = "Roms/Chip8 Picture.ch8";
// file = "Roms/Life [GV Samways, 1980].ch8";

var program = File.ReadAllBytes(file);
var renderer = new Chip8Renderer();
renderer.RunDesktop();

class Chip8Renderer : Simulation
{
    Chip8? chip8;
    ITexture display;
    List<string> files = new();
    bool isBreakpointSet = false;

    public Chip8Renderer()
    {
    }

    public override void OnInitialize(AppConfig config)
    {
        display = Graphics.CreateTexture(Chip8.DisplayWidth, Chip8.DisplayHeight);

        Keyboard.KeyPressed += Keyboard_KeyPressed;
        Keyboard.KeyReleased += Keyboard_KeyReleased;

        foreach (var file in Directory.GetFiles("./Roms", "*.ch8", SearchOption.AllDirectories))
        {
            files.Add(file);
        }
    }

    private void Keyboard_KeyReleased(Key key)
    {
        byte? keyByte = MapKey(key);

        if (keyByte is not null)
            chip8?.SetKeyState(keyByte.Value, false);
    }

    private void Keyboard_KeyPressed(Key key)
    {
        byte? keyByte = MapKey(key);

        if (keyByte is not null)
            chip8?.SetKeyState(keyByte.Value, true);
    }

    public override void OnRender(ICanvas canvas)
    {
        if (ImGui.Begin("Roms"))
        {
            foreach (var file in files)
            {
                if (ImGui.Selectable(file))
                {
                    chip8 = new(File.ReadAllBytes(file));
                }
            }
        }
        ImGui.End();
        
        if (ImGui.Begin("Debugger")) 
        {
            if (isBreakpointSet)
            {
                ImGui.Text("Breakpoint is set; press J to continue, k to step");
            }
            else
            {
                ImGui.Text("Breakpoint not set; press J to set");
            }
        }
        ImGui.End();

        if (Keyboard.IsKeyPressed(Key.J))
        {
            isBreakpointSet = !isBreakpointSet;
        }

        canvas.Clear(Color.FromHSV(0,0,.1f));
        if (!isBreakpointSet)
            chip8?.Cycle();

        if (chip8 is not null && chip8.wantRedraw)
        {
            for (int y = 0; y < Chip8.DisplayHeight; y++)
            {
                for (int x = 0; x < Chip8.DisplayWidth; x++)
                {
                    ref Color pixel = ref display.GetPixel(x, y);

                    byte b = chip8.memory[Chip8.VideoMemoryLocation + (y * Chip8.DisplayWidth + x)/8];

                    pixel = BitUtils.ExtractBit(b, 7-(x%8)) is 1 ? Color.White : Color.Black;
                }
            }

            display.ApplyChanges();
            chip8.wantRedraw = false;
        }

        canvas.Translate(canvas.Width / 2, canvas.Height / 2);
        canvas.Scale(10);
        canvas.DrawTexture(display, Alignment.Center);
    }

    private byte? MapKey(Key key) => key switch
    {
        Key.Key1 => 0x1,
        Key.Key2 => 0x2,
        Key.Key3 => 0x3,
        Key.Key4 => 0xC,

        Key.Q => 0x4,
        Key.W => 0x5,
        Key.E => 0x6,
        Key.R => 0xD,

        Key.A => 0x7,
        Key.S => 0x8,
        Key.D => 0x9,
        Key.F => 0xE,

        Key.Z => 0xA,
        Key.X => 0x0,
        Key.C => 0xB,
        Key.V => 0xF,

        _ => null
    };
}

class Chip8
{
    public const int RegisterCount = 16;
    public const int MemorySize = 4096;
    public const int DisplayWidth = 64;
    public const int DisplayHeight = 32;
    public const int VideoMemoryLocation = 0x100;
    public const int VideoMemorySize = 0x100;
    public const int ProgramMemory = 0x200;
    public const int FontSetLocation = 0x004D;
    public const int FontSetSize = 80;
    public const int FontSetDigits = 16;
    public const int FontSetDigitSize = FontSetSize / FontSetDigits;


    byte[] registers = new byte[RegisterCount];
    public byte[] memory = new byte[MemorySize];
    ushort sp;
    ushort ip;
    ushort I;
    
    byte time;
    float timeSinceLastTick = 0;
    public bool wantRedraw;
    bool isWaitingForKey = false;
    bool[] keyStates = new bool[16];
    byte keyWaitRegister;

    static readonly byte[] fontSet = new byte[]
    {
        0xF0, 0x90, 0x90, 0x90, 0xF0, // 0
        0x20, 0x60, 0x20, 0x20, 0x70, // 1
        0xF0, 0x10, 0xF0, 0x80, 0xF0, // 2
        0xF0, 0x10, 0xF0, 0x10, 0xF0, // 3
        0x90, 0x90, 0xF0, 0x10, 0x10, // 4
        0xF0, 0x80, 0xF0, 0x10, 0xF0, // 5
        0xF0, 0x80, 0xF0, 0x90, 0xF0, // 6
        0xF0, 0x10, 0x20, 0x40, 0x40, // 7
        0xF0, 0x90, 0xF0, 0x90, 0xF0, // 8
        0xF0, 0x90, 0xF0, 0x10, 0xF0, // 9
        0xF0, 0x90, 0xF0, 0x90, 0x90, // A
        0xE0, 0x90, 0xE0, 0x90, 0xE0, // B
        0xF0, 0x80, 0x80, 0x80, 0xF0, // C
        0xE0, 0x90, 0x90, 0x90, 0xE0, // D
        0xF0, 0x80, 0xF0, 0x80, 0xF0, // E
        0xF0, 0x80, 0xF0, 0x80, 0x80  // F 
    };

    public Chip8(byte[] program)
    {
        ip = ProgramMemory;
        program.CopyTo(memory, ip);
        fontSet.CopyTo(memory, FontSetLocation);
        wantRedraw = true;
    }

    public void Cycle()
    {
        timeSinceLastTick += Time.DeltaTime;

        if (timeSinceLastTick >= (1f/60f))
        {
            time--;
            timeSinceLastTick = 0;
        }

        if (!isWaitingForKey)
        {
            ushort instruction = (ushort)((memory[ip] << 8) | memory[ip + 1]);
            ip += 2;
            Eval(instruction);
        }
    }

    private void Eval(ushort instruction)
    {
        ushort op = (ushort)((instruction & 0xF000) >> 12);
        byte x = (byte)((instruction & 0x0F00) >> 8);
        byte y = (byte)((instruction & 0x00F0) >> 4);
        ushort nnn = (ushort)(instruction & 0x0FFF);
        byte nn = (byte)(instruction & 0x00FF);
        byte n = (byte)(instruction & 0x00F);

        switch (op)
        {
            case 0:
                switch (nn)
                {
                    case 0x00:
                        Trace("NOP");
                        break;
                    case 0xE0:
                        Trace("CLEAR");
                        Array.Clear(memory, VideoMemoryLocation, VideoMemorySize);
                        wantRedraw = true;
                        break;
                    case 0xEE:
                        Trace("RETURN");
                        sp -= 2;
                        ip = (ushort)((memory[sp] << 8) | memory[sp+1]);
                        break;
                    default:
                        throw new Exception("unknown instruction");
                }
                break;
            case 1:
                Trace($"GOTO {nnn:x}");
                ip = nnn;
                break;
            case 2:
                Trace($"CALL {nnn:x}");
                memory[sp] = (byte)(ip>>8);
                memory[sp+1] = (byte)(ip&0xFF);
                sp += 2;
                ip = nnn;
                break;
            case 3:
                Trace($"SKIP IF REG({x:x}) == {nn:x}");
                
                if (registers[x] == nn)
                    ip += 2;

                break;
            case 4:
                Trace($"SKIP IF REG({x:x}) != {nn:x}");

                if (registers[x] != nn)
                    ip += 2;

                break;
            case 5:
                Trace($"SKIP IF REG({x:x}) == REG({y:x})");

                if (registers[x] == registers[y])
                    ip += 2;

                break;
            case 6:
                Trace($"REG({x:x}) = {nn:x}");
                registers[x] = nn;
                break;
            case 7:
                Trace($"REG({x:x}) += {nn:x}");
                registers[x] += nn;
                break;
            case 8:
                // used for carry info, usually placed in VF
                byte b;
                switch (instruction & 0x000F)
                {
                    case 0:
                        Trace($"REG({x:x}) = REG({y:x})");
                        registers[x] = registers[y];
                        break;
                    case 1:
                        Trace($"REG({x:x}) |= REG({y:x})");
                        registers[x] |= registers[y];
                        break;
                    case 2:
                        Trace($"REG({x:x}) &= REG({y:x})");
                        registers[x] &= registers[y];
                        break;
                    case 3:
                        Trace($"REG({x:x}) ^= REG({y:x})");
                        registers[x] ^= registers[y];
                        break;
                    case 4:
                        Trace($"REG({x:x}) += REG({y:x}); REG(F)={registers[0xF]:x}");
                        var addition = registers[x] + registers[y];
                        registers[x] = (byte)addition;
                        registers[0xF] = (byte)(addition > 255 ? 1 : 0);
                        break;
                    case 5:
                        Trace($"REG({x:x}) -= REG({y:x}); REG(F)={registers[0xF]:x}");
                        b = (byte)(registers[x] < registers[y] ? 0 : 1);
                        registers[x] -= registers[y];
                        registers[0xF] = b;
                        break;
                    case 6:
                        Trace($"V{x:x} >>= 1; VF = {registers[x] & 0x1}");
                        b = (byte)(registers[x] & 0x1);
                        registers[x] >>= 1;
                        registers[0xF] = b;
                        break;
                    case 7:
                        Trace($"V{x:x} = V{y:x} - V{x:x}; VF = {(registers[y] < registers[x] ? 1 : 0)}");
                        b = (byte)(registers[y] < registers[x] ? 0 : 1);
                        registers[x] = (byte)(registers[y] - registers[x]);
                        registers[0xF] = b;
                        break;
                    case 0xE:
                        Trace($"V{x:x} <<= 1; VF = {registers[x] & 0x1}");
                        b = (byte)(registers[x] & 0x1);
                        registers[x] <<= 1;
                        b = registers[0xF];
                        break;
                    default:
                        throw new Exception("unknown instruction");
                }
                break;
            case 9:
                Trace($"SKIP IF REG({x:x}) != REG({y:x})");

                if (registers[x] != registers[y])
                    ip += 2;

                break;
            case 10:
                Trace($"I = {nnn:x}");
                I = nnn;
                break;
            case 11:
                Trace($"GOTO {nnn+registers[0]:x} ({nnn:x}+{registers[0]:x})");
                ip = (ushort)(nnn + registers[0]);
                break;
            case 12:
                Trace($"REG({x:x}) = [RND] & {nn:x}");
                registers[x] = (byte)(nn & Random.Shared.Next(0, 255));
                break;
            case 13:
                Trace($"SHOW SPRITE({I:x}) AT ({registers[x]:x},{registers[y]:x}); REG(F)={registers[0xF]:x}");
                wantRedraw = true;
                for (int i = 0; i < n; i++)
                {
                    ref byte sprite = ref memory[I + i];
                    ref byte display = ref memory[VideoMemoryLocation + ((i + registers[y]) * DisplayWidth + registers[x]) / 8];

                    for (int j = 0; j < 8; j++)
                    {
                        byte spriteBit = BitUtils.ExtractBit(sprite, j);
                        if (spriteBit is 1)
                        {
                            byte displayBit = BitUtils.ExtractBit(display, j);

                            if (displayBit is 1)
                            {
                                registers[0xF] = 1;
                            }

                            display = BitUtils.SetBit(display, j, spriteBit ^ displayBit);
                        }
                    }
                }
                break;
            case 14:
                switch (nn)
                {
                    case 0x9E:
                        Trace($"SKIP IF KEY == REG({x:x})");
                        throw new NotImplementedException();
                        break;
                    case 0xA1:
                        Trace($"SKIP IF KEY != REG({x:x})");
                        throw new NotImplementedException();
                        break;
                    default:
                        throw new Exception("unknown instruction");
                }
                break;
            case 15:
                switch (nn)
                {
                    case 0x00:
                        Trace("STOP");
                        throw new NotImplementedException();
                        break;
                    case 0x07:
                        Trace($"V{x:x}=TIME");
                        registers[x] = time;
                        break;
                    case 0x0A:
                        Trace($"V{x:x}=KEY");
                        isWaitingForKey = true;
                        keyWaitRegister = x;
                        break;
                    case 0x15:
                        Trace($"TIME=V{x:x}");
                        time = registers[x];
                        break;
                    case 0x17:
                        Trace("PITCH=VX");
                        // throw new NotImplementedException();
                        break;
                    case 0x18:
                        Trace("TONE=VX");
                        // throw new NotImplementedException();
                        break;
                    case 0x1E:
                        Trace("I+=VX");
                        I += registers[x];
                        break;
                    case 0x29:
                        Trace($"GET DIGIT SPRITE V{x:x}");
                        I = (ushort)(FontSetLocation + registers[x] * 5);
                        break;
                    case 0x33:
                        Trace("STORE BCD");
                        byte value = registers[x];
                        memory[I+0] = (byte)(value / 100);
                        memory[I+1] = (byte)((value % 100) / 10);
                        memory[I+2] = (byte)(value % 10);
                        break;
                    case 0x55:
                        Trace("REG DUMP");
                        for (int i = 0; i <= x; i++)
                        {
                            this.memory[this.I + i] = registers[i];
                        }
                        break;
                    case 0x65:
                        Trace("REG LOAD");
                        for (int i = 0; i <= x; i++)
                        {
                            registers[i] = this.memory[this.I + i];
                        }
                        break;
                    default:
                        throw new Exception("unknown instruction");
                }
                break;
            default:
                throw new Exception("unknown instruction");
        }
    }

    [Conditional("DEBUG")]
    private void Trace(string trace)
    {
        Console.WriteLine($"{(ip-2):x}: {trace}");
    }

    public void SetKeyState(byte key, bool pressed)
    {
        if (key > 0xF)
            throw new Exception();

        if (isWaitingForKey && pressed && !keyStates[key])
        {
            registers[keyWaitRegister] = key;
            isWaitingForKey = false;
        }

        keyStates[key] = pressed;
    }
}

class BitUtils
{
    public static byte ExtractBit(byte b, int bit)
    {
        return (byte)((b >> bit) & 0x1);
    }

    public static byte SetBit(byte display, int bit, int value)
    {
        return (byte)((display & ~(1<<bit)) | (value << bit));
    }
}