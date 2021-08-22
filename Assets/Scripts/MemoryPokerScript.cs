﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using KModkit;
using Rnd = UnityEngine.Random;

public class MemoryPokerScript : MonoBehaviour {

    public KMBombInfo Bomb;
    public KMAudio Audio;
    public KMBombModule Module;
    public Card[] cards;
    public Sprite[] sprites;
    static int moduleIdCounter = 1;
    int moduleId;
    private bool moduleSolved;
    private bool uninteractable;
    private static readonly string[] coordinates = { "A1", "B1", "C1", "D1", "A2", "B2", "C2", "D2", "A3", "B3", "C3", "D3", "A4", "B4", "C4", "D4" };

    private PuzzleGenerator generator = new PuzzleGenerator();
    private PokerHandCalculator handCalc = new PokerHandCalculator();
    private CardInfo[] startingGrid = new CardInfo[16];
private int[] initiallyFaceUp = Enumerable.Range(0, 16).ToArray();
    private int[] tableCards;
    private int[] originalPositions;
    private int stage = 0;
    private const int STAGE_COUNT = 3;
    private bool startingPhase = true;

    private CardInfo[] actualCards = new CardInfo[3];

    private Suit[] suitTable;
    private Rank[] rankTable;
    private bool currentToSuit;

    void Awake () {
        moduleId = moduleIdCounter++;
        for (int i = 0; i < 16; i++)
        {
            int ix = i;
            cards[ix].selectable.OnInteract += delegate () { CardPress(ix); return false; };
        }

        Module.OnActivate += delegate () { StartCoroutine(Activate()); } ;

        //Button.OnInteract += delegate () { ButtonPress(); return false; };

    }
    void Start ()
    {
        generator.CreateGrid();
        for (int i = 0; i < 16; i++)
        {
            startingGrid[i] = new CardInfo() { suit = generator.suitGrid[i], rank = generator.rankGrid[i] };
            cards[i].Info = startingGrid[i];
        }
        handCalc.grid = (CardInfo[])startingGrid.Clone();
        DetermineGrids();

        Log("The starting grid is:");
        Log(startingGrid.Take(4).Join());
        Log(startingGrid.Skip(4).Take(4).Join());
        Log(startingGrid.Skip(8).Take(4).Join());
        Log(startingGrid.Skip(12).Take(4).Join());

    }

    void DetermineGrids()
    {
        if (Bomb.GetBatteryCount() == 3 && Bomb.GetBatteryHolderCount() == 2)
        {
            Log("Table case 1 used.");
            rankTable = Tables.rankTable;
            suitTable = Tables.suitTable;
            currentToSuit = false;
        }
        else if (Bomb.GetSerialNumber().Contains('S') || Bomb.GetSerialNumber().Contains('G'))
        {
            Log("Table case 2 used.");
            rankTable = Tables.rankTable;
            suitTable = Tables.suitTable;
            currentToSuit = true;
        }
        else if (Bomb.GetPortPlates().Any(x => x.Length == 0))
        {
            Log("Table case 3 used.");
            rankTable = TransformationHandler.ApplyTransformation(Tables.rankTable, Rotation.ninetyCW).ToArray();
            suitTable = TransformationHandler.ApplyTransformation(Tables.suitTable, Rotation.ninetyCW).ToArray();
            currentToSuit = Bomb.GetPortCount() == 0;
        }
        else if (Bomb.GetIndicators().Count() >= 2)
        {
            Log("Table case 4 used.");
            string[] firstInds = Bomb.GetIndicators().OrderBy(x => x).Take(2).ToArray();
            if (Bomb.GetOnIndicators().Contains(firstInds[0]))
                rankTable = TransformationHandler.ApplyTransformation(Tables.rankTable, Rotation.none, Reflection.X);
            else rankTable = TransformationHandler.ApplyTransformation(Tables.rankTable, Rotation.hundredEightyCW);
            if (Bomb.GetOnIndicators().Contains(firstInds[1]))
                suitTable = TransformationHandler.ApplyTransformation(Tables.suitTable, Rotation.none, Reflection.X);
            else suitTable = TransformationHandler.ApplyTransformation(Tables.suitTable, Rotation.hundredEightyCW);
            currentToSuit = Bomb.GetIndicators().Join("").Distinct().Count() != 3 * Bomb.GetIndicators().Count();
        }
        else
        {
            Log("Table case 5 used.");
            int snSum = Bomb.GetSerialNumber().Select(x => "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".IndexOf(x)).Sum();
            int ccwCount = (4 - (snSum % 3 + 1)) % 4; //Since rotations are implemented based on cw directions, to turn the ccw we need to do some shenanigans with subtracting from 4.
            rankTable = TransformationHandler.ApplyTransformation(Tables.rankTable, (Rotation)ccwCount);
            suitTable = TransformationHandler.ApplyTransformation(Tables.suitTable, (Rotation)ccwCount);
            currentToSuit = snSum / 10 % 2 == snSum % 2;
        }
        Log("The rank table used is: " + rankTable.Select(x => x.ToString()[0]).Join());
        Log("The suit table used is: " + suitTable.Select(x => "♠♥♣♦"[(int)x]).Join());
        Log(string.Format("The {0} table is used for the current positions, and the {1} table is used for the initial positions", 
            currentToSuit ? "suit" : "rank", currentToSuit ? "rank" : "suit"));
    }


    void CardPress(int pos)
    {
        if (cards[pos].animating || uninteractable)
            return;
        if (moduleSolved)
            cards[pos].Flip();
        else if (startingPhase)
            StartCoroutine(InitializeStage());
        else if (!cards[pos].faceUp)
        {
            Log(string.Format("Flipped {0} ({1})", coordinates[pos], cards[pos].Info));
            cards[pos].Flip();
            handCalc.Add(cards[pos].Info);
            if (cards.Count(x => x.faceUp) == 5)
                StartCoroutine(Submit());
        }
    }
    IEnumerator Activate()
    {
        uninteractable = true;
        for (int i = 0; i < 16; i++)
            cards[i].UpdateAppearance();
        yield return FlipMultiple(initiallyFaceUp.Select(x => cards[x]));
        uninteractable = false;
    }

    IEnumerator InitializeStage()
    {
        uninteractable = true;
        yield return new WaitUntil(() => cards.All(x => !x.animating));
        stage++;
        yield return FlipMultiple(cards.Where(x => x.faceUp));
        yield return new WaitForSeconds(0.5f);
        for (int i = 0; i < 16; i++)
            cards[i].UpdateAppearance();
        yield return GenerateStage();
        startingPhase = false;
        uninteractable = false;
    }
    IEnumerator Submit()
    {
        uninteractable = true;
        yield return new WaitUntil(() => cards.All(c => !c.animating));
        PokerHand currentHand = handCalc.CalculateHand(handCalc.hand);
        Log("Submitted cards " + handCalc.hand.Join());
        Log("Submitted a " + currentHand.ToString().Replace('_', ' '));
        if (currentHand == handCalc.bestHand)
        {
            if (stage == STAGE_COUNT)
            {
                yield return FlipMultiple(cards.Where(x => x.faceUp));
                moduleSolved = true;
                Module.HandlePass();
                uninteractable = false;
            }
            else
            {
                handCalc.hand.Clear();
                for (int i = 0; i < 16; i++)
                    cards[i].Info = startingGrid[i];
                Log("That was correct. Progressing to stage " + (stage + 1));
                yield return InitializeStage();
            }
        }
        else
        {
            handCalc.hand.Clear();
            Log("That was incorrect. Resetting stage.");
            StartCoroutine(Strike());
        }
    }

    IEnumerator GenerateStage()
    {
        GetTablePlacements();
        GetActualCards();
        handCalc.GetBestHand(tableCards);
        Log(string.Format("The best possible poker hand is a {0}, which is obtainable with cards {1}.", handCalc.bestHand.ToString().Replace('_', ' '), handCalc.bestHandSet.Join()));
        yield return FlipMultiple(tableCards.Select(x => cards[x]));
    }
    void GetTablePlacements()
    {
        tableCards = Enumerable.Range(0, 16).ToArray().Shuffle().Take(3).ToArray();
        originalPositions = Enumerable.Range(0, 16).ToArray().Shuffle().Take(3).ToArray();
        for (int i = 0; i < 3; i++)
        {
            int pos = tableCards[i];
            cards[pos].Info = cards[originalPositions[i]].Info;
            cards[pos].UpdateAppearance();
            Log(string.Format("Card {0} flipped over to reveal a {1}.", coordinates[pos], cards[pos].ToString()));
        }
    }
    void GetActualCards()
    {
        handCalc.hand.Clear();
        for (int i = 0; i < 3; i++)
        {
            actualCards[i] = new CardInfo()
            {
                suit = suitTable[currentToSuit ? tableCards[i] : originalPositions[i]],
                rank = rankTable[!currentToSuit ? tableCards[i] : originalPositions[i]],
            };
            handCalc.Add(actualCards[i]);
            Log(string.Format("The card at {0} maps to {1}.", coordinates[tableCards[i]], actualCards[i]));
        }
    }

    IEnumerator FlipMultiple(IEnumerable<Card> cards)
    {
        foreach (Card card in cards)
        {
            card.Flip();
            yield return new WaitForSeconds(0.125f);
        }
    }
    IEnumerator Strike()
    {
        yield return new WaitUntil(() => cards.All(x => !x.animating));
        Module.HandleStrike();
        uninteractable = true;
        yield return FlipMultiple(cards.Where(x => x.faceUp));
        yield return new WaitUntil(() => cards.All(x => !x.animating));
        for (int i = 0; i < 16; i++)
        {
            cards[i].Info = startingGrid[i];
            cards[i].UpdateAppearance();
        }
        yield return new WaitForSeconds(0.5f);
        yield return FlipMultiple(cards);
        startingPhase = true;
        uninteractable = false;
    }
    
    void Log(string msg)
    {
        Debug.LogFormat("[Memory Poker #{0}] {1}", moduleId, msg);
    }

    #pragma warning disable 414
    private readonly string TwitchHelpMessage = @"Use <!{0} foobar> to do something.";
    #pragma warning restore 414

    IEnumerator ProcessTwitchCommand (string command)
    {
        StartCoroutine(Strike());
        command = command.Trim().ToUpperInvariant();
        List<string> parameters = command.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        yield return null;
    }

    IEnumerator TwitchHandleForcedSolve ()
    {
        while (uninteractable)
            yield return true;
        if (startingPhase)
            cards.PickRandom().selectable.OnInteract();
        yield return new WaitForSeconds(0.125f);
        while (!moduleSolved)
        {
            while (uninteractable)
                yield return true;
            foreach (CardInfo card in handCalc.bestHandSet.Skip(3))
            {
                int pos = Enumerable.Range(0, 16).First(x => startingGrid[x].suit == card.suit && startingGrid[x].rank == card.rank);
                Debug.Log(pos);
                cards[pos].selectable.OnInteract();
                yield return new WaitForSeconds(0.125f);
            }
            yield return null;
        }
    }
}