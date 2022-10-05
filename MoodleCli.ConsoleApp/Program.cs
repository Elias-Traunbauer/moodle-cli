﻿using MoodleCli.Core.Model;
using MoodleCli.Core.Model.Reponses;
using MoodleCli.Core.Services;
using Spectre.Console;

namespace MoodleCli.ConsoleApp
{
    internal class Program
    {
        public static string? MoodleUser => Environment.GetEnvironmentVariable("MOODLE_USER");
        public static string? MoodlePassword => Environment.GetEnvironmentVariable("MOODLE_PASSWORD");

        static async Task Main(string[] args)
        {
            using IMoodleService moodleService = new MoodleService(
                username: MoodleUser ?? throw new ArgumentNullException("MOODLE_USER"),
                password: MoodlePassword ?? throw new ArgumentNullException("MOODLE_PASSWORD"));
            ICompilerService compilerService = new CompilerService();

            User currentUser = await GetCurrentMoodleUser(moodleService);
            Course[] courses = await LoadCourses(moodleService, currentUser);

            int courseId = LetUserChooseFromCourses(courses);
            Course selectedCourse = courses.Single(entry => entry.Id == courseId);
            AnsiConsole.Write(new Markup($"You choose the course [green]{selectedCourse.FullName}[/]!"));

            Assignment[] assignments = await LoadAssignments(moodleService, selectedCourse);
            int assignmentId = LetUserChooseFromAssignments(assignments);

            Assignment selectedAssignment = assignments.Single(entry => entry.Id == assignmentId);
            AnsiConsole.Write(new Markup($"You choose the assignment [green]{selectedAssignment.Name}[/]!"));

            SubmissionFile[] submissions = await LoadSubmissions(moodleService, selectedAssignment);
            AnsiConsole.Write(new Markup($"Found [green]{submissions.Length} submissions[/]!"));

            (SubmissionFile, Stream)[] downloadedFiles = await DownloadSubmissionFiles(moodleService, submissions);

            AnsiConsole.Write(await GenerateResultTableAsync(compilerService, downloadedFiles));
        }

        private async static Task<Table> GenerateResultTableAsync(
            ICompilerService compilerService,
            (SubmissionFile, Stream)[] downloadedFiles)
        {
            return await AnsiConsole
                 .Status()
                 .StartAsync("Compiling the files ...",
                 async ctx =>
                 {
                     var table = new Table();

                     table.AddColumn("Schüler");
                     table.AddColumn("Dateiname");
                     table.AddColumn(new TableColumn("Größe").Centered());
                     table.AddColumn(new TableColumn("Fehler").Centered());
                     table.AddColumn(new TableColumn("Warnungen").Centered());

                     foreach (var submission in downloadedFiles)
                     {
                         (int CntErrors, int CntWarnings, List<string> ErrorMessages) = await compilerService.CompileAsync(submission.Item2);

                         table.AddRow(
                               $"[blue]{submission.Item1.UserId}[/]"
                             , $"{submission.Item1.Filename}"
                             , $"{submission.Item1.Size} B"
                             , $"{GetColoredMarkupForNumber(CntErrors, "red")}"
                             , $"{GetColoredMarkupForNumber(CntWarnings, "yellow")}");
                     }

                     return table;

                     static string GetColoredMarkupForNumber(int number, string color)
                     {
                         if (number == 0)
                         {
                             return $"[green]{number}[/]";
                         }
                         else
                         {
                             return $"[{color}]{number}[/]";
                         }
                     }
                 });
        }

        

        private static async Task<(SubmissionFile, Stream)[]> DownloadSubmissionFiles(IMoodleService moodleService, SubmissionFile[] submissions)
        {
            return await AnsiConsole
                .Status()
                .StartAsync("Downloading the files ...",
                async ctx => await DownloadSubmissionFilesAsync(moodleService, submissions));
        }

        private static async Task<(SubmissionFile, Stream)[]> DownloadSubmissionFilesAsync(IMoodleService moodleService, SubmissionFile[] submissions)
        {
            List<Task<(SubmissionFile, Stream)>> downloads = new List<Task<(SubmissionFile, Stream)>>();
            foreach (var submission in submissions)
            {
                downloads.Add(moodleService.DownloadSubmissionFileAsync(submission));
            }

            return await Task.WhenAll(downloads.ToArray());
        }

        private static async Task<SubmissionFile[]> LoadSubmissions(IMoodleService moodleService, Assignment selectedAssignment)
        {
            return await AnsiConsole
                .Status()
                .StartAsync("Loading submissions...", async ctx => await moodleService.GetSubmissionsForAssignmentAsync(selectedAssignment.Id));
        }

        private static int LetUserChooseFromAssignments(Assignment[] assignments)
        {
            return AnsiConsole.Prompt(
                new SelectionPrompt<int>() { Converter = (int id) => assignments.Single(assigment => assigment.Id == id).Name! }
                    .Title("Which [green]assignment[/] do you want to choose?")
                    .PageSize(10)
                    .MoreChoicesText("[grey](Move up and down to reveal more assignments)[/]")
                    .AddChoices(assignments.Select(entry => entry.Id)));
        }

        private static async Task<Assignment[]> LoadAssignments(IMoodleService moodleService, Course selectedCourse)
        {
            return await AnsiConsole
                            .Status()
                            .StartAsync("Loading assignments...", async ctx => await moodleService.GetAssignmentsForCourse(selectedCourse.Id));
        }

        private static int LetUserChooseFromCourses(Course[] courses)
        {
            return AnsiConsole.Prompt(
                            new SelectionPrompt<int>() { Converter = (int courseId) => courses.Single(course => course.Id == courseId).ShortName! }
                                .Title("Which [green]course[/] do you want to choose?")
                                .PageSize(10)
                                .MoreChoicesText("[grey](Move up and down to reveal more courses)[/]")
                                .AddChoices(courses.Select(entry => entry.Id)));
        }

        private static async Task<Course[]> LoadCourses(IMoodleService moodleService, User currentUser)
        {
            return await AnsiConsole
                .Status()
                .StartAsync("Loading courses...", async ctx => await moodleService.GetUsersCoursesAsync(currentUser.Id));
        }

        private static async Task<User> GetCurrentMoodleUser(IMoodleService moodleService)
        {
            return await AnsiConsole
                .Status()
                .StartAsync("Loading user data...",
                async ctx => await moodleService.GetCurrentUsersInfos() ?? throw new Exception("There was a problem loading the user details"));
        }
    }
}