# CLAUDE.md

# Presentation Management System

You are the lead software architect for this project.

This is a production-quality enterprise application.

Never generate demo code.

Always generate scalable, maintainable and clean code.

---

# Project Overview

Presentation Management System is a Windows Forms application used in corporate meetings.

The application manages employee presentations, timers, discussions, presentation queue and presentation screens.

The system must feel like enterprise software.

---

# Technology

.NET 8

C#

Windows Forms

Entity Framework Core

SQL Server

ASP.NET Core Web API

SignalR

Telegram Bot API

Microsoft.Office.Interop.PowerPoint

Dependency Injection

Repository Pattern

Clean Architecture

SOLID Principles

Async/Await

---

# Architecture

Always follow Clean Architecture.

Projects:

PresentationManager.Domain

PresentationManager.Application

PresentationManager.Infrastructure

PresentationManager.API

PresentationManager.WinForms

Never mix UI with business logic.

Never access database directly from WinForms.

Business logic belongs to Application layer.

Database belongs to Infrastructure.

UI only calls Services.

---

# UI Philosophy

This application is displayed on

Projectors

Large TVs

Meeting rooms

Never create old-style WinForms interfaces.

The UI must look modern.

Minimal.

Corporate.

Professional.

Inspired by Microsoft Fluent Design.

---

# Theme

Dark Theme

Background

#14161D

Cards

#23252F

Primary Color

#FFC52D

Text

White

Secondary Text

#BDBDBD

Danger

#E53935

Success

#43A047

Warning

#FB8C00

---

# Typography

Only use

Segoe UI

Title

34-42 pt

Bold

Section Title

18-22 pt

Semibold

Body

11-12 pt

Timer

110-150 pt

Bold

---

# Layout Rules

Never randomly position controls.

Always use

TableLayoutPanel

FlowLayoutPanel

Dock

Anchor

Consistent spacing.

Outer margin

24px

Control spacing

12px

Section spacing

20px

Everything must be visually balanced.

---

# Buttons

Flat Style

Rounded

10px radius

Height

44-50px

No classic WinForms button style.

Hover state

Pressed state

Disabled state

Always implemented.

---

# Forms

Use DoubleBuffered.

Borderless for presentation screens.

Support DPI Scaling.

Support Full HD.

Support 4K.

Support second monitor.

---

# Presentation Screen

Presentation Screen is NOT Admin Screen.

Presentation Screen displays only

Presenter

Department

Presentation Title

Large Timer

Current Mode

Next Presenter

Nothing else.

No controls.

No debug information.

---

# Timer Engine

Presentation Timer

Discussion Timer

They are completely independent.

Presentation can be paused.

Discussion starts.

Discussion ends.

Presentation resumes from remaining time.

Never restart Presentation Timer.

---

# Alarm Rules

When remaining time reaches

10

9

8

...

1

Play warning sound every second.

At

00

Play final alarm.

Alarm must never freeze UI.

Use asynchronous playback.

---

# Queue

Support

Next

Previous

Skip

Finished

Waiting

Running

Paused

Discussion

Completed

---

# PowerPoint

Support

ppt

pptx

pdf

Automatically open files.

Automatically close previous presentation.

Never leave orphan PowerPoint processes.

---

# Telegram Bot

Telegram Bot uploads presentations.

Bot sends

Full Name

Department

Presentation Title

Presentation File

Bot never writes directly into database.

Bot always calls API.

---

# API

WinForms never accesses SQL Server directly.

Everything goes through API.

Use REST.

Return DTOs.

Never expose Entity objects.

---

# SQL

Use Entity Framework Core.

Use migrations.

No raw SQL unless absolutely necessary.

---

# Logging

Log

Errors

Warnings

Presentation Started

Presentation Finished

Discussion Started

Discussion Finished

Queue Changes

---

# Error Handling

Never swallow exceptions.

Always log.

Always show friendly error messages.

---

# Code Style

Meaningful names.

No abbreviations.

No magic numbers.

Use constants.

Small methods.

Single Responsibility.

---

# Comments

Comment only when business logic is not obvious.

Avoid useless comments.

---

# Performance

UI must never freeze.

Long operations

Upload

Download

PowerPoint

Database

Always asynchronous.

---

# Before Writing Code

Always

1.

Analyze requirement.

2.

Suggest architecture.

3.

Explain approach.

4.

Generate code.

5.

Review generated code.

6.

Suggest improvements.

---

# Self Review

Before finishing every response ask yourself:

Is this production quality?

Is UI modern?

Does it follow Clean Architecture?

Does it follow SOLID?

Can this code scale?

If not,

Improve it before answering.

---

# Never Do

Never generate outdated WinForms UI.

Never put SQL inside Form.

Never put business logic inside Button Click.

Never duplicate code.

Never generate placeholder code.

Never skip error handling.

Never ignore async.

---

# Goal

Build software that looks and feels like enterprise conference management software used by Microsoft, Google or large corporations.

Every screen must look premium.

Every line of code must be production-ready.