mergeInto(LibraryManager.library, { 
     RequestFullScreen: function () { 
         var canvas = document.getElementById('#canvas'); 
         if (!canvas) { 
             canvas = document.getElementsByTagName('canvas')[0]; 
         } 
 
         if (canvas.requestFullscreen) { 
             canvas.requestFullscreen(); 
         } else if (canvas.webkitRequestFullscreen) { // Safari 
             canvas.webkitRequestFullscreen(); 
         } else if (canvas.msRequestFullscreen) { // IE11 
             canvas.msRequestFullscreen(); 
         } 
     } 
 }); 
