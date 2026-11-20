

        var loginForm = document.querySelector('form');

        if (loginForm) {

            loginForm.addEventListener('submit', function () {

                try { localStorage.setItem('mygts_last_activity', Date.now().toString()); } catch (_) {}

            });

        }



        function toggleLoginPassword(btn) {

            const input = document.getElementById('LoginPassword');

            if (input) {

                if (input.type === 'password') {

                    input.type = 'text';

                    btn.style.color = '#0056b3';

                } else {

                    input.type = 'password';

                    btn.style.color = '#666';

                }

            }

        }

    