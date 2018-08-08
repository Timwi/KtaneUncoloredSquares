using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UncoloredSquares;
using UnityEngine;

using Rnd = UnityEngine.Random;

/// <summary>
/// On the Subject of Uncolored Squares
/// Created by Timwi
/// </summary>
public class UncoloredSquaresModule : MonoBehaviour
{
    public KMBombInfo Bomb;
    public KMBombModule Module;
    public KMAudio Audio;

    public KMSelectable[] Buttons;
    public Material[] Materials;
    public Material BlackMaterial;
    public Light LightTemplate;

    private Light[] _lights;
    private SquareColor[] _colors;
    private readonly Color[] _lightColors = new[] { Color.white, Color.red, Color.green, new Color(131f / 255, 131f / 255, 1f), Color.yellow, Color.magenta, Color.white };

    private SquareColor _firstStageColor1;   // for Souvenir
    private SquareColor _firstStageColor2;   // for Souvenir

    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private Coroutine _activeCoroutine;
    private readonly List<int> _squaresPressedThisStage = new List<int>();
    private readonly List<List<int>> _permissiblePatterns = new List<List<int>>();
    private bool _isSolved;

    private static readonly bool[][][,] _table = newArray(
        new bool[][,] { null, b("##|#"), b(" #|##"), b("#|##|#"), b("##| #") },
        new bool[][,] { b("#|#"), null, b("#|#|##"), b("##|##"), b(" ##|##") },
        new bool[][,] { b("#|##| #"), b("#|##"), null, b("###| #"), b(" #|###") },
        new bool[][,] { b(" #|##|#"), b("##|#|#"), b(" #| #|##"), null, b(" #|##| #") },
        new bool[][,] { b("##| ##"), b("###|  #"), b("##"), b("###|#"), null }
    );

    private static bool[,] b(string v)
    {
        var rows = v.Split('|');
        var w = rows.Max(row => row.Length);
        var h = rows.Length;
        var arr = new bool[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (x < rows[y].Length)
                    arr[x, y] = rows[y][x] == '#';
        return arr;
    }

    static T[] newArray<T>(params T[] array) { return array; }

    void Start()
    {
        _moduleId = _moduleIdCounter++;

        float scalar = transform.lossyScale.x;
        _lights = new Light[16];
        _colors = new SquareColor[16];

        for (int i = 0; i < 16; i++)
        {
            var j = i;
            Buttons[i].OnInteract += delegate { Pushed(j); return false; };
            Buttons[i].GetComponent<MeshRenderer>().material = BlackMaterial;
            var light = _lights[i] = (i == 0 ? LightTemplate : Instantiate(LightTemplate));
            light.name = "Light" + (i + 1);
            light.transform.parent = Buttons[i].transform;
            light.transform.localPosition = new Vector3(0, 0.08f, 0);
            light.transform.localScale = new Vector3(1, 1, 1);
            light.gameObject.SetActive(false);
            light.range = .1f * scalar;
        }

        SetStage(true);
    }

    private void SetStage(bool isStart)
    {
        var squaresToRecolor = Enumerable.Range(0, 16).Where(ix => isStart || _colors[ix] != SquareColor.Black).ToList();

        if (isStart)
        {
            for (int i = 0; i < 16; i++)
            {
                _colors[i] = SquareColor.White;
                Buttons[i].GetComponent<MeshRenderer>().material = BlackMaterial;
                _lights[i].gameObject.SetActive(false);
            }
        }
        else
        {
            var sq = 0;
            for (int i = 0; i < 16; i++)
            {
                switch (_colors[i])
                {
                    case SquareColor.Black:
                        break;

                    case SquareColor.Red:
                    case SquareColor.Green:
                    case SquareColor.Blue:
                    case SquareColor.Yellow:
                    case SquareColor.Magenta:
                        Buttons[i].GetComponent<MeshRenderer>().material = BlackMaterial;
                        _lights[i].gameObject.SetActive(false);
                        sq++;
                        break;

                    case SquareColor.White:
                        _colors[i] = SquareColor.Black;
                        break;
                }
            }
            if (sq <= 3)
            {
                Pass();
                return;
            }
        }

        // Discover all color combinations that have a valid pattern placement
        var all = new Dictionary<SquareColor, HashSet<SquareColor>>();
        for (int first = 0; first < 5; first++)
            for (int second = 0; second < 5; second++)
                if (first != second)
                {
                    var pattern0 = _table[second][first];
                    var w = pattern0.GetLength(0);
                    var h = pattern0.GetLength(1);
                    for (int i = 0; i < 16; i++)
                    {
                        if (i % 4 + w > 4 || i / 4 + h > 4)
                            continue;
                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++)
                            {
                                var ix = i % 4 + x + 4 * (i / 4 + y);
                                if (_colors[ix] == SquareColor.Black)
                                    goto nope;
                            }
                        var f = (SquareColor) (first + 1);
                        var s = (SquareColor) (second + 1);
                        if (!all.ContainsKey(f))
                            all[f] = new HashSet<SquareColor>();
                        all[f].Add(s);
                        break;
                        nope:;
                    }
                }
        if (all.Count == 0)
        {
            Pass();
            return;
        }

        // Fill the still-lit squares with “codes” (numbers 0–5 that we will later map to actual colors)
        int[] counts;
        int minCount;
        int[] minCountCodes;
        int[] colorCodes = new int[16];
        do
        {
            counts = new int[5];
            for (int i = 0; i < 16; i++)
                if (_colors[i] != SquareColor.Black)
                {
                    var col = Rnd.Range(0, 5);
                    colorCodes[i] = col;
                    counts[col]++;
                }
                else
                    colorCodes[i] = -1;
            minCount = counts.Where(c => c > 0).Min();
            minCountCodes = Enumerable.Range(0, 5).Where(code => counts[code] == minCount).OrderBy(c => Array.IndexOf(colorCodes, c)).ToArray();
        }
        while (minCountCodes.Length != 2);

        // Pick a color combination at random
        var keys = all.Keys.ToArray();
        var firstColor = keys[Rnd.Range(0, keys.Length)];
        keys = all[firstColor].ToArray();
        var secondColor = keys[Rnd.Range(0, keys.Length)];

        // Create the map from color code to actual color in such a way that the chosen colors are in the correct place
        var allColors = new List<SquareColor> { SquareColor.Blue, SquareColor.Green, SquareColor.Magenta, SquareColor.Red, SquareColor.Yellow };
        allColors.Remove(firstColor);
        allColors.Remove(secondColor);
        if (minCountCodes[0] > minCountCodes[1])
        {
            allColors.Insert(minCountCodes[1], secondColor);
            allColors.Insert(minCountCodes[0], firstColor);
        }
        else
        {
            allColors.Insert(minCountCodes[0], firstColor);
            allColors.Insert(minCountCodes[1], secondColor);
        }

        // Assign the colors
        for (int i = 0; i < 16; i++)
            if (_colors[i] != SquareColor.Black)
                _colors[i] = allColors[colorCodes[i]];

        if (isStart)
        {
            _firstStageColor1 = firstColor;
            _firstStageColor2 = secondColor;
        }

        // Determine all possible locations where the pattern can be placed
        _squaresPressedThisStage.Clear();
        _permissiblePatterns.Clear();
        {
            var pattern0 = _table[(int) secondColor - 1][(int) firstColor - 1];
            var w = pattern0.GetLength(0);
            var h = pattern0.GetLength(1);
            for (int i = 0; i < 16; i++)
            {
                if (i % 4 + w > 4 || i / 4 + h > 4)
                    continue;
                var l = new List<int>();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (pattern0[x, y])
                        {
                            var ix = i % 4 + x + 4 * (i / 4 + y);
                            if (_colors[ix] == SquareColor.Black)
                                goto nope;
                            l.Add(ix);
                        }
                _permissiblePatterns.Add(l);
                nope:;
            }
        }

        if (_activeCoroutine != null)
            StopCoroutine(_activeCoroutine);
        _activeCoroutine = StartCoroutine(SetSquareColors(squaresToRecolor));

        if (isStart)
            Debug.LogFormat("[Uncolored Squares #{0}] First stage color pair is {1}/{2}.", _moduleId, _firstStageColor1, _firstStageColor2);
        else
            Debug.LogFormat("[Uncolored Squares #{0}] Next stage color pair is {1}/{2}.", _moduleId, firstColor, secondColor);
    }

    private static string JoinString<T>(IEnumerable<T> values, string separator = null, string prefix = null, string suffix = null, string lastSeparator = null)
    {
        if (values == null)
            throw new ArgumentNullException("values");
        if (lastSeparator == null)
            lastSeparator = separator;

        using (var enumerator = values.GetEnumerator())
        {
            if (!enumerator.MoveNext())
                return "";

            // Optimise the case where there is only one element
            var one = enumerator.Current;
            if (!enumerator.MoveNext())
                return prefix + one + suffix;

            // Optimise the case where there are only two elements
            var two = enumerator.Current;
            if (!enumerator.MoveNext())
            {
                // Optimise the (common) case where there is no prefix/suffix; this prevents an array allocation when calling string.Concat()
                if (prefix == null && suffix == null)
                    return one + lastSeparator + two;
                return prefix + one + suffix + lastSeparator + prefix + two + suffix;
            }

            StringBuilder sb = new StringBuilder()
                .Append(prefix).Append(one).Append(suffix).Append(separator)
                .Append(prefix).Append(two).Append(suffix);
            var prev = enumerator.Current;
            while (enumerator.MoveNext())
            {
                sb.Append(separator).Append(prefix).Append(prev).Append(suffix);
                prev = enumerator.Current;
            }
            sb.Append(lastSeparator).Append(prefix).Append(prev).Append(suffix);
            return sb.ToString();
        }
    }

    private void Pass()
    {
        if (_activeCoroutine != null)
            StopCoroutine(_activeCoroutine);
        Debug.LogFormat("[Uncolored Squares #{0}] Module solved.", _moduleId);
        Module.HandlePass();
        _isSolved = true;
        for (int i = 0; i < 16; i++)
        {
            Buttons[i].GetComponent<MeshRenderer>().material = BlackMaterial;
            _lights[i].gameObject.SetActive(false);
        }
    }

    private IEnumerator SetSquareColors(List<int> indexes)
    {
        yield return new WaitForSeconds(1.75f);
        shuffle(indexes);
        for (int i = 0; i < indexes.Count; i++)
        {
            SetSquareColor(indexes[i]);
            yield return new WaitForSeconds(.03f);
        }
        _activeCoroutine = null;
    }

    private static IList<T> shuffle<T>(IList<T> list)
    {
        if (list == null)
            throw new ArgumentNullException("list");
        for (int j = list.Count; j >= 1; j--)
        {
            int item = Rnd.Range(0, j);
            if (item < j - 1)
            {
                var t = list[item];
                list[item] = list[j - 1];
                list[j - 1] = t;
            }
        }
        return list;
    }

    void SetSquareColor(int index)
    {
        Buttons[index].GetComponent<MeshRenderer>().material = Materials[(int) _colors[index]];
        _lights[index].color = _lightColors[(int) _colors[index]];
        _lights[index].gameObject.SetActive(_colors[index] != SquareColor.Black);
    }

    void Pushed(int index)
    {
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, Buttons[index].transform);
        Buttons[index].AddInteractionPunch();

        if (_isSolved)
            return;

        if (!_permissiblePatterns.Any(p => p.Contains(index)))
        {
            Debug.LogFormat(@"[Uncolored Squares #{0}] Button {1}{2} was incorrect at this time. Resetting module.", _moduleId, "ABCD"[index % 4], "1234"[index / 4]);
            Module.HandleStrike();
            if (_activeCoroutine != null)
                StopCoroutine(_activeCoroutine);
            SetStage(true);
        }
        else
        {
            switch (_colors[index])
            {
                case SquareColor.Red:
                    Audio.PlaySoundAtTransform("redlight", Buttons[index].transform);
                    break;
                case SquareColor.Blue:
                    Audio.PlaySoundAtTransform("bluelight", Buttons[index].transform);
                    break;
                case SquareColor.Green:
                    Audio.PlaySoundAtTransform("greenlight", Buttons[index].transform);
                    break;
                case SquareColor.Yellow:
                    Audio.PlaySoundAtTransform("yellowlight", Buttons[index].transform);
                    break;
                case SquareColor.Magenta:
                    Audio.PlaySoundAtTransform("magentalight", Buttons[index].transform);
                    break;
            }

            _permissiblePatterns.RemoveAll(lst => !lst.Contains(index));
            _squaresPressedThisStage.Add(index);
            _colors[index] = SquareColor.White;
            SetSquareColor(index);

            for (int i = 0; i < 16; i++)
                if (_colors[i] == SquareColor.Black)
                {
                    Buttons[i].GetComponent<MeshRenderer>().material = BlackMaterial;
                    _lights[i].gameObject.SetActive(false);
                }

            if (_permissiblePatterns.Count == 1 && _squaresPressedThisStage.Count == _permissiblePatterns[0].Count)
                SetStage(false);
        }
    }

#pragma warning disable 414
    private readonly string TwitchHelpMessage = @"Press the desired squares with “!{0} A1 A2 A3 B3”.";
#pragma warning restore 414

    IEnumerable<KMSelectable> ProcessTwitchCommand(string command)
    {
        var buttons = new List<KMSelectable>();
        foreach (var piece in command.ToLowerInvariant().Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (piece.Length != 2 || piece[0] < 'a' || piece[0] > 'd' || piece[1] < '1' || piece[1] > '4')
                return null;
            buttons.Add(Buttons[(piece[0] - 'a') + 4 * (piece[1] - '1')]);
        }
        return buttons;
    }
}
