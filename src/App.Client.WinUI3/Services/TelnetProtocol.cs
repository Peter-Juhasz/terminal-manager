using System.Buffers;
using System.Text;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Separates what a Telnet server sends into the data, which is what the shell writes, and the commands of the protocol, which it answers, as a client that only asks for little:
/// the server may echo and suppress go-ahead, and this client suppresses go-ahead and tells its terminal type, which is <see cref="TerminalType"/>; every other option is refused.
/// </summary>
/// <remarks>
/// An option is only answered when it changes, so the client and the server don't answer each other forever, as RFC 854 asks.
/// A carriage return followed by a null, which is how a server sends one on its own, is read as a carriage return.
/// </remarks>
public sealed class TelnetProtocol
{
    /// <summary>The terminal type told to the server, which programs write no colors or cursor movements to.</summary>
    public const string TerminalType = "dumb";

    internal const byte Se = 240;
    internal const byte Sb = 250;
    internal const byte Will = 251;
    internal const byte Wont = 252;
    internal const byte Do = 253;
    internal const byte Dont = 254;
    internal const byte Iac = 255;

    internal const byte EchoOption = 1;
    internal const byte SuppressGoAheadOption = 3;
    internal const byte TerminalTypeOption = 24;

    private const byte TerminalTypeIs = 0;
    private const byte TerminalTypeSend = 1;

    // more than any subnegotiation answered here needs, so one that is sent without an end doesn't take up memory without bound
    private const int MaxSubnegotiationLength = 256;

    private readonly HashSet<byte> _enabledHere = [];
    private readonly HashSet<byte> _enabledThere = [];
    private readonly HashSet<byte> _refusedHere = [];
    private readonly HashSet<byte> _refusedThere = [];
    private readonly List<byte> _subnegotiation = [];

    private State _state;
    private byte _verb;
    private bool _afterCarriageReturn;

    private enum State
    {
        Data,
        Command,
        Option,
        Subnegotiation,
        SubnegotiationCommand,
    }

    /// <summary>
    /// Reads <paramref name="received"/>, which continues what was read before, writing the data in it to <paramref name="data"/>,
    /// and what is sent back to the server for the commands in it to <paramref name="reply"/>.
    /// </summary>
    public void Read(ReadOnlySpan<byte> received, IBufferWriter<byte> data, IBufferWriter<byte> reply)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(reply);

        foreach (var value in received)
        {
            switch (_state)
            {
                case State.Data when value is Iac:
                    _state = State.Command;
                    break;

                // what follows a carriage return is only a null when the return is on its own, and was already written
                case State.Data when value is 0 && _afterCarriageReturn:
                    _afterCarriageReturn = false;
                    break;

                case State.Data:
                    _afterCarriageReturn = value is (byte)'\r';
                    data.Write([value]);
                    break;

                // a doubled one is the byte itself
                case State.Command when value is Iac:
                    _afterCarriageReturn = false;
                    data.Write([value]);
                    _state = State.Data;
                    break;

                case State.Command when value is Will or Wont or Do or Dont:
                    _verb = value;
                    _state = State.Option;
                    break;

                case State.Command when value is Sb:
                    _subnegotiation.Clear();
                    _state = State.Subnegotiation;
                    break;

                // the others, such as go ahead, don't ask for anything
                case State.Command:
                    _state = State.Data;
                    break;

                case State.Option:
                    Negotiate(_verb, value, reply);
                    _state = State.Data;
                    break;

                case State.Subnegotiation when value is Iac:
                    _state = State.SubnegotiationCommand;
                    break;

                case State.Subnegotiation:
                    if (_subnegotiation.Count < MaxSubnegotiationLength)
                    {
                        _subnegotiation.Add(value);
                    }

                    break;

                case State.SubnegotiationCommand when value is Se:
                    Subnegotiate(reply);
                    _state = State.Data;
                    break;

                // a doubled one is the byte itself, within the subnegotiation
                case State.SubnegotiationCommand:
                    if (_subnegotiation.Count < MaxSubnegotiationLength)
                    {
                        _subnegotiation.Add(value);
                    }

                    _state = State.Subnegotiation;
                    break;
            }
        }
    }

    /// <summary>The bytes sent for <paramref name="text"/>, typed in: each line feed, which ends a line, is sent as a carriage return followed by a line feed, as Telnet ends lines.</summary>
    /// <remarks>UTF-8 never has a byte that is 255, so nothing in it needs to be doubled.</remarks>
    public static byte[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n").Replace("\n", "\r\n", StringComparison.Ordinal));
    }

    private void Negotiate(byte verb, byte option, IBufferWriter<byte> reply)
    {
        switch (verb)
        {
            case Do when option is SuppressGoAheadOption or TerminalTypeOption:
                if (_enabledHere.Add(option))
                {
                    reply.Write([Iac, Will, option]);
                }

                break;

            case Do:
                if (_refusedHere.Add(option))
                {
                    reply.Write([Iac, Wont, option]);
                }

                break;

            case Dont:
                if (_enabledHere.Remove(option))
                {
                    reply.Write([Iac, Wont, option]);
                }

                break;

            case Will when option is EchoOption or SuppressGoAheadOption:
                if (_enabledThere.Add(option))
                {
                    reply.Write([Iac, Do, option]);
                }

                break;

            case Will:
                if (_refusedThere.Add(option))
                {
                    reply.Write([Iac, Dont, option]);
                }

                break;

            case Wont:
                if (_enabledThere.Remove(option))
                {
                    reply.Write([Iac, Dont, option]);
                }

                break;
        }
    }

    private void Subnegotiate(IBufferWriter<byte> reply)
    {
        if (_subnegotiation is not [TerminalTypeOption, TerminalTypeSend] || !_enabledHere.Contains(TerminalTypeOption))
        {
            return;
        }

        reply.Write([Iac, Sb, TerminalTypeOption, TerminalTypeIs]);
        reply.Write(Encoding.ASCII.GetBytes(TerminalType));
        reply.Write([Iac, Se]);
    }
}
