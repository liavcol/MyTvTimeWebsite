﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using MyTvTime.Data;
using MyTvTime.Models;
using Newtonsoft.Json;
using RestSharp;

namespace MyTvTime.Controllers
{
    public class MoviesController : Controller
    {
        private readonly UserContext db;

        public MoviesController(UserContext context)
        {
            db = context;
        }

        // GET: Movies
        public async Task<IActionResult> Index(string title)
        {
            var movies = from m in db.Movie select m;

            if (!string.IsNullOrEmpty(title))
            {
                movies = movies.Where(s => s.Name.Contains(title));
                List<Movie> res = await movies.ToListAsync();
                if (!res.Any())
                    await AddFromIMDBAsync(title);

                return View(res);
            }


           // ViewBag.watchList = await db.UserMovie.Where(um => um.UserId == 1).Include(m=> m.Movie).ToListAsync();
            return View(await movies.ToListAsync());
        }

        // GET: Movies
        public async Task<IActionResult> WatchList()
        {
            var myWatchList = await db.UserMovie.Where(um => um.UserId == 1).Include(m => m.Movie).ToListAsync();
            return View(myWatchList);
        }

        private async Task AddFromIMDBAsync(string search)
        {
            var client = new RestClient("https://rapidapi.p.rapidapi.com/?title=" + search + "&type=get-movies-by-title");
            var request = new RestRequest(Method.GET);
            request.AddHeader("x-rapidapi-host", "movies-tvshows-data-imdb.p.rapidapi.com");
            request.AddHeader("x-rapidapi-key", "93f2bbe6bdmsh4a12d4d9e5b771dp14b702jsn8bc0f393b0ca");
            var response = await client.ExecuteAsync(request);

            MovieResultRoot movieResultRoot = JsonConvert.DeserializeObject<MovieResultRoot>(response.Content);

            for (int i = 0; i < 5; i++)
            {
                if (!db.Movie.Where(m => m.IMDBID == movieResultRoot.movie_results[i].imdb_id).Any())
                    await AddSingleMovieIMDBAsync(movieResultRoot.movie_results[i].imdb_id);
            }
            
        }

        private async Task AddSingleMovieIMDBAsync(string IMDBID)
        {
            var client = new RestClient("https://rapidapi.p.rapidapi.com/?imdb=" + IMDBID + "&type=get-movie-details");
            var request = new RestRequest(Method.GET);
            request.AddHeader("x-rapidapi-host", "movies-tvshows-data-imdb.p.rapidapi.com");
            request.AddHeader("x-rapidapi-key", "93f2bbe6bdmsh4a12d4d9e5b771dp14b702jsn8bc0f393b0ca");
            var response = await client.ExecuteAsync(request);

            IMDBMovieDetails md = JsonConvert.DeserializeObject<IMDBMovieDetails>(response.Content);

            /*Parsing a release date or runtime that are not in correct format can result in crashing the website,
             So we use the try parsing methods that won't raise exceptions. Also IMDB are storing lots of other things that are not really movies and we don't want to add
            those to our DB, so we check to see if the movie we got is above 80 min long and that it actually have genres stored for it, 
            because the chances of it being a legit movie are much higher.*/
            DateTime date;
            int run = int.TryParse(md.runtime, out run) ? run : default;
            if (run <= 80 || md.genres == null)
                return;

            Movie m = new Movie
            {
                IMDBID = md.imdb_id,
                Name = md.title,
                ReleaseDate = DateTime.TryParse(md.release_date, out date) ? date : default,
                Language = md.language[0],
                Runtime = run,
                Description = md.description,
            };
            await GetMovieImageAsync(m);
            
            await db.Movie.AddAsync(m);
            await db.SaveChangesAsync();

            //Handle the genres
            foreach (string s in md.genres)
            {
                var genres = from g in db.Genre where (g.Name == s) select g;
                if (!genres.Any())
                {
                    await db.Genre.AddAsync(new Genre { Name = s });
                    await db.SaveChangesAsync();
                    genres = from g in db.Genre where (g.Name == s) select g;
                }

                await db.MovieGenres.AddAsync(new MovieGenres { MovieID = m.ID, GenreID = genres.First().ID });
            }
            await db.SaveChangesAsync();
        }

        private async Task GetMovieImageAsync(Movie movie)
        {
            var client = new RestClient("https://rapidapi.p.rapidapi.com/?imdb=" + movie.IMDBID + "&type=get-movies-images-by-imdb");
            var request = new RestRequest(Method.GET);
            request.AddHeader("x-rapidapi-host", "movies-tvshows-data-imdb.p.rapidapi.com");
            request.AddHeader("x-rapidapi-key", "93f2bbe6bdmsh4a12d4d9e5b771dp14b702jsn8bc0f393b0ca");
            var response = await client.ExecuteAsync(request);

            ImageResult imageResult = JsonConvert.DeserializeObject<ImageResult>(response.Content);
            
            movie.ImageURL = imageResult.poster;
        }

        // GET: Movies/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var movie = await db.Movie
                .FirstOrDefaultAsync(m => m.ID == id);
            if (movie == null)
            {
                return NotFound();
            }

            var movieWatched = await db.UserMovie.FirstOrDefaultAsync(um => (um.MovieId == id && um.UserId == int.Parse("1")));
            if (movieWatched == null)
            {
                ViewBag.inWatchList = false;


            } else
            {
                ViewBag.inWatchList = true;
            }




            return View(movie);
        }

        // GET: Movies/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Movies/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to, for 
        // more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ID,IMDBID,Name,ReleaseDate,Language,Runtime,Description,ImageURL")] Movie movie)
        {
            if (ModelState.IsValid)
            {
                db.Add(movie);
                await db.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(movie);
        }

        [HttpPost]
        public async Task<IActionResult> CreateIMDB(string jsonData)
        {
            Movie m = JsonConvert.DeserializeObject<Movie>(jsonData);
            if (ModelState.IsValid)
            {
                //_context.Add(recievedData);
                await db.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View();
        }

        // GET: Movies/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var movie = await db.Movie.FindAsync(id);
            if (movie == null)
            {
                return NotFound();
            }
            return View(movie);
        }

        // POST: Movies/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to, for 
        // more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("ID,IMDBID,Name,ReleaseDate,Language,Runtime,Description,ImageURL")] Movie movie)
        {
            if (id != movie.ID)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    db.Update(movie);
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!MovieExists(movie.ID))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            return View(movie);
        }

        // GET: Movies/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var movie = await db.Movie
                .FirstOrDefaultAsync(m => m.ID == id);
            if (movie == null)
            {
                return NotFound();
            }

            return View(movie);
        }

        // POST: Movies/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var movie = await db.Movie.FindAsync(id);
            db.Movie.Remove(movie);
            await db.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool MovieExists(int id)
        {
            return db.Movie.Any(e => e.ID == id);
        }

        [HttpPost, ActionName("AddToWatchList")]
        public async Task<IActionResult> AddToWatchList(string movieId, string username, string isExist)
		{
            if (isExist == "0")
            {
                try
                {
                    await db.UserMovie.AddAsync(new UserMovie { MovieId = int.Parse(movieId), UserId = int.Parse(username) });
                    await db.SaveChangesAsync();
                }
                catch (Exception err)
                {
                    Console.WriteLine(err.Message);
                }
            }
            else
			{
                var userMovie = await db.UserMovie.FirstOrDefaultAsync(um=> (um.MovieId == int.Parse(movieId) && um.UserId == int.Parse(username)));

                if(userMovie != null)
				{
                    db.UserMovie.Remove(userMovie);
                    await db.SaveChangesAsync();
                }

            }

           // var user = await db.User.Where(um => um.Id == int.Parse(username)).Include(um => um.Watchlist).ThenInclude(wl => wl.Movie).FirstOrDefaultAsync();

			return RedirectToAction("Details", new { id = int.Parse(movieId) });
        }
    }
}
